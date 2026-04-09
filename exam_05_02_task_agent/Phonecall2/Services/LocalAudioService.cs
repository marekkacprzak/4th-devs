using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Phonecall2.Config;
using Phonecall2.UI;

namespace Phonecall2.Services;

/// <summary>
/// Handles TTS (text → MP3) and STT (audio → text) using local services:
///
/// TTS: macOS say -v Zosia → AIFF → MP3 via ffmpeg
///      Results are cached in tts_cache/ directory by SHA256 of text.
/// STT: WhisperKit server at localhost:50060 (OpenAI-compatible /v1/audio/transcriptions)
///      Results are cached in tts_cache/ directory by SHA256 of audio content.
/// </summary>
public class LocalAudioService
{
    private readonly HttpClient _http;
    private readonly AudioConfig _config;
    private readonly RunLogger _logger;
    private readonly string _cacheDir;

    public LocalAudioService(HttpClient http, AudioConfig config, RunLogger logger)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
        _config = config;
        _logger = logger;
        _cacheDir = Path.Combine(AppContext.BaseDirectory, "tts_cache");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>Synthesizes Polish text to MP3 bytes via macOS say (Zosia), with file cache.</summary>
    public async Task<byte[]> TextToSpeechAsync(string text)
    {
        var cacheKey = ComputeCacheKey(text, "zosia");
        var cachePath = Path.Combine(_cacheDir, $"{cacheKey}.mp3");
        if (File.Exists(cachePath))
        {
            var cached = await File.ReadAllBytesAsync(cachePath);
            _logger.LogInfo($"TTS cache hit: {cachePath} ({cached.Length} bytes)");
            ConsoleUI.PrintPhase($"TTS (cached): \"{text[..Math.Min(60, text.Length)]}...\"");
            return cached;
        }

        ConsoleUI.PrintPhase($"TTS (Zosia): \"{text[..Math.Min(60, text.Length)]}...\"");
        _logger.LogInfo($"TTS request (say Zosia): {text[..Math.Min(80, text.Length)]}");

        var mp3Bytes = await MacOsSayToMp3Async(text);

        await File.WriteAllBytesAsync(cachePath, mp3Bytes);
        _logger.LogInfo($"TTS cached to: {cachePath}");

        return mp3Bytes;
    }

    private static async Task<byte[]> MacOsSayToMp3Async(string text)
    {
        using var aiffFile = new TempFile(".aiff");
        using var mp3File = new TempFile(".mp3");

        var sayPsi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "say",
            Arguments = $"-v Zosia -o \"{aiffFile.Path}\" \"{text.Replace("\"", "'")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var sayProcess = System.Diagnostics.Process.Start(sayPsi)
            ?? throw new InvalidOperationException("Failed to start say");
        await sayProcess.WaitForExitAsync();
        if (sayProcess.ExitCode != 0)
        {
            var stderr = await sayProcess.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"say failed (exit {sayProcess.ExitCode}): {stderr}");
        }

        return await ConvertAiffToMp3Async(aiffFile.Path, mp3File.Path);
    }

    private static async Task<byte[]> ConvertAiffToMp3Async(string aiffPath, string mp3Path)
    {
        string ffmpegPath = FindFfmpeg();
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-y -i \"{aiffPath}\" -codec:a libmp3lame -q:a 2 \"{mp3Path}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"ffmpeg failed: {stderr[..Math.Min(300, stderr.Length)]}");
        }
        return await File.ReadAllBytesAsync(mp3Path);
    }

    private static string FindFfmpeg()
    {
        foreach (var c in new[] { "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "ffmpeg" })
            if (c == "ffmpeg" || File.Exists(c)) return c;
        throw new InvalidOperationException("ffmpeg not found. Install via: brew install ffmpeg");
    }

    /// <summary>Transcribes audio bytes to text via WhisperKit STT, with cache.</summary>
    public async Task<string> SpeechToTextAsync(byte[] audioBytes, string mimeType = "audio/wav")
    {
        var sttCacheKey = ComputeCacheKey(
            Convert.ToBase64String(audioBytes[..Math.Min(256, audioBytes.Length)]), "stt");
        var sttCachePath = Path.Combine(_cacheDir, $"stt_{sttCacheKey}.txt");
        if (File.Exists(sttCachePath))
        {
            var cachedTranscript = await File.ReadAllTextAsync(sttCachePath);
            _logger.LogInfo($"STT cache hit: {sttCachePath} → \"{cachedTranscript}\"");
            ConsoleUI.PrintPhase($"STT (cached): \"{cachedTranscript[..Math.Min(60, cachedTranscript.Length)]}\"");
            return cachedTranscript;
        }

        var extension = mimeType switch
        {
            "audio/wav" => ".wav",
            "audio/mpeg" or "audio/mp3" => ".mp3",
            "audio/flac" => ".flac",
            "audio/m4a" or "audio/mp4" => ".m4a",
            _ => ".wav"
        };

        var url = $"{_config.WhisperKitEndpoint}/v1/audio/transcriptions";
        _logger.LogInfo($"STT request (WhisperKit): mimeType={mimeType}, audioBytes={audioBytes.Length}");
        ConsoleUI.PrintPhase("STT: transcribing operator audio (WhisperKit)...");

        // Use an explicit simple boundary — some servers (Vapor/WhisperKit) reject
        // the GUID boundaries with quotes that .NET generates by default.
        const string boundary = "WhisperKitBoundary";
        using var content = new MultipartFormDataContent(boundary);
        var fileContent = new ByteArrayContent(audioBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
        content.Add(fileContent, "file", $"audio{extension}");
        content.Add(new StringContent("whisper-1"), "model");
        content.Add(new StringContent("pl"), "language");
        content.Add(new StringContent("json"), "response_format");

        // .NET quotes the boundary in Content-Type; Vapor requires it unquoted.
        content.Headers.Remove("Content-Type");
        content.Headers.TryAddWithoutValidation("Content-Type", $"multipart/form-data; boundary={boundary}");

        var response = await _http.PostAsync(url, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var err = $"WhisperKit STT error {(int)response.StatusCode}: {responseBody[..Math.Min(300, responseBody.Length)]}";
            _logger.LogError("STT", err);
            throw new InvalidOperationException(err);
        }

        using var doc = JsonDocument.Parse(responseBody);
        var transcript = doc.RootElement.GetProperty("text").GetString()?.Trim()
            ?? throw new InvalidOperationException("No text in WhisperKit STT response");

        _logger.LogInfo($"STT transcript: {transcript}");
        ConsoleUI.PrintConversation("Operator", transcript);

        await File.WriteAllTextAsync(sttCachePath, transcript);
        _logger.LogInfo($"STT cached to: {sttCachePath}");

        return transcript;
    }

    private static string ComputeCacheKey(string text, string voice)
    {
        var input = $"{voice}|{text}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16].ToLower();
    }
}

/// <summary>Creates a temporary file that is deleted on disposal.</summary>
file sealed class TempFile : IDisposable
{
    public string Path { get; }

    public TempFile(string ext)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"phonecall2_{Guid.NewGuid():N}{ext}");
    }

    public void Dispose()
    {
        try { if (File.Exists(Path)) File.Delete(Path); } catch { }
    }
}
