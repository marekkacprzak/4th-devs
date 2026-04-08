using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Phonecall.Config;
using Phonecall.UI;

namespace Phonecall.Services;

/// <summary>
/// Handles TTS (text → MP3) and STT (audio → text) via Google Gemini REST API.
///
/// TTS: gemini-2.5-flash-preview-tts → returns raw PCM L16 (24kHz, 16-bit, mono)
///      → wrapped in WAV header → converted to MP3 via ffmpeg
///      Results are cached in tts_cache/ directory by SHA256 of (text + voice).
/// STT: gemini-2.5-flash multimodal → inline_data with base64 audio → returns transcript
/// </summary>
public class GeminiAudioService
{
    private const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    private readonly HttpClient _http;
    private readonly AudioConfig _config;
    private readonly RunLogger _logger;
    private readonly string _cacheDir;

    public GeminiAudioService(HttpClient http, AudioConfig config, RunLogger logger)
    {
        // Use a dedicated client with a longer timeout — Gemini audio requests can be slow
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
        _config = config;
        _logger = logger;
        _cacheDir = Path.Combine(AppContext.BaseDirectory, "tts_cache");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>Synthesizes Polish text to MP3 bytes via Gemini TTS, with file cache.</summary>
    public async Task<byte[]> TextToSpeechAsync(string text)
    {
        // Check cache first
        var cacheKey = ComputeCacheKey(text, _config.TtsVoice);
        var cachePath = Path.Combine(_cacheDir, $"{cacheKey}.mp3");
        if (File.Exists(cachePath))
        {
            var cached = await File.ReadAllBytesAsync(cachePath);
            _logger.LogInfo($"TTS cache hit: {cachePath} ({cached.Length} bytes)");
            ConsoleUI.PrintPhase($"TTS (cached): \"{text[..Math.Min(60, text.Length)]}...\"");
            return cached;
        }

        var apiKey = _config.GetApiKey();
        var url = $"{GeminiBaseUrl}/{_config.TtsModel}:generateContent?key={apiKey}";

        var body = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text } } }
            },
            generationConfig = new
            {
                responseModalities = new[] { "AUDIO" },
                speechConfig = new
                {
                    voiceConfig = new
                    {
                        prebuiltVoiceConfig = new
                        {
                            voiceName = _config.TtsVoice
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(body);
        _logger.LogInfo($"TTS request: voice={_config.TtsVoice}, text={text[..Math.Min(80, text.Length)]}...");
        ConsoleUI.PrintPhase($"TTS: \"{text[..Math.Min(60, text.Length)]}...\"");

        // Retry up to 3 times for transient 429s
        HttpResponseMessage response = null!;
        string responseBody = "";
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            response = await _http.PostAsync(url,
                new StringContent(json, Encoding.UTF8, "application/json"));
            responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode) break;

            if ((int)response.StatusCode == 429)
            {
                // Parse retryDelay from Gemini response
                int delaySec = 15;
                try
                {
                    using var errDoc = JsonDocument.Parse(responseBody);
                    var retryDelayStr = errDoc.RootElement
                        .GetProperty("error").GetProperty("details")
                        .EnumerateArray()
                        .Where(d => d.TryGetProperty("retryDelay", out _))
                        .Select(d => d.GetProperty("retryDelay").GetString() ?? "15s")
                        .FirstOrDefault() ?? "15s";
                    delaySec = int.Parse(retryDelayStr.TrimEnd('s')) + 2;
                }
                catch { }

                if (attempt < 3)
                {
                    _logger.LogInfo($"TTS 429 rate limited, waiting {delaySec}s before retry {attempt + 1}/3...");
                    ConsoleUI.PrintPhase($"TTS rate limited — waiting {delaySec}s...");
                    await Task.Delay(TimeSpan.FromSeconds(delaySec));
                    continue;
                }
            }

            break;
        }

        if (!response.IsSuccessStatusCode)
        {
            // Quota exhausted — try other Gemini TTS models, then macOS say as last resort
            if ((int)response.StatusCode == 429)
            {
                // Try gemini-2.0-flash-preview-tts (separate quota from gemini-2.5-flash-tts)
                string[] ttsFallbackModels = ["gemini-2.0-flash-preview-tts", "gemini-1.5-flash-preview-tts"];
                foreach (var fallbackTtsModel in ttsFallbackModels)
                {
                    _logger.LogInfo($"TTS quota exhausted, trying {fallbackTtsModel}...");
                    ConsoleUI.PrintPhase($"TTS: trying {fallbackTtsModel} fallback...");
                    var fallbackTtsUrl = $"{GeminiBaseUrl}/{fallbackTtsModel}:generateContent?key={_config.GetApiKey()}";
                    try
                    {
                        var fallbackTtsResp = await _http.PostAsync(fallbackTtsUrl,
                            new StringContent(json, Encoding.UTF8, "application/json"));
                        var fallbackTtsBody = await fallbackTtsResp.Content.ReadAsStringAsync();
                        _logger.LogInfo($"TTS {fallbackTtsModel}: HTTP {(int)fallbackTtsResp.StatusCode}");
                        if (fallbackTtsResp.IsSuccessStatusCode)
                        {
                            using var fallbackDoc = JsonDocument.Parse(fallbackTtsBody);
                            var fbInline = fallbackDoc.RootElement
                                .GetProperty("candidates")[0].GetProperty("content")
                                .GetProperty("parts")[0].GetProperty("inlineData");
                            var fbBase64 = fbInline.GetProperty("data").GetString()
                                ?? throw new InvalidOperationException("No audio in TTS fallback response");
                            var fbPcm = Convert.FromBase64String(fbBase64);
                            var fbWav = WrapPcmAsWav(fbPcm);
                            var fbMp3 = await ConvertWavToMp3Async(fbWav);
                            await File.WriteAllBytesAsync(cachePath, fbMp3);
                            _logger.LogInfo($"TTS {fallbackTtsModel} cached to: {cachePath}");
                            return fbMp3;
                        }
                    }
                    catch (Exception ex) { _logger.LogInfo($"TTS {fallbackTtsModel} failed: {ex.Message}"); }
                }

                _logger.LogInfo("All Gemini TTS models exhausted, falling back to macOS say (Zosia)");
                ConsoleUI.PrintPhase("TTS: Gemini quota exhausted, using macOS say (Zosia)...");
                var fallbackMp3 = await MacOsSayToMp3Async(text);
                await File.WriteAllBytesAsync(cachePath, fallbackMp3);
                _logger.LogInfo($"TTS (say fallback) cached to: {cachePath}");
                return fallbackMp3;
            }

            var err = $"Gemini TTS error {(int)response.StatusCode}: {responseBody[..Math.Min(300, responseBody.Length)]}";
            _logger.LogError("TTS", err);
            throw new InvalidOperationException(err);
        }

        using var doc = JsonDocument.Parse(responseBody);
        var inlineData = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("inlineData");

        var mimeType = inlineData.GetProperty("mimeType").GetString() ?? "audio/pcm";
        var audioBase64 = inlineData.GetProperty("data").GetString()
            ?? throw new InvalidOperationException("No audio data in TTS response");

        var pcmBytes = Convert.FromBase64String(audioBase64);
        _logger.LogInfo($"TTS received: mimeType={mimeType}, pcmBytes={pcmBytes.Length}");

        // Gemini TTS returns raw L16 PCM (24kHz, 16-bit, mono).
        // Wrap in WAV, then convert to MP3 via ffmpeg for best compatibility.
        var wavBytes = WrapPcmAsWav(pcmBytes);
        var mp3Bytes = await ConvertWavToMp3Async(wavBytes);
        _logger.LogInfo($"TTS converted to MP3: {mp3Bytes.Length} bytes");

        // Save to cache
        await File.WriteAllBytesAsync(cachePath, mp3Bytes);
        _logger.LogInfo($"TTS cached to: {cachePath}");

        return mp3Bytes;
    }

    private static string ComputeCacheKey(string text, string voice)
    {
        var input = $"{voice}|{text}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16].ToLower();
    }

    /// <summary>Uses macOS say command with Polish voice "Zosia" to synthesize MP3.</summary>
    private static async Task<byte[]> MacOsSayToMp3Async(string text)
    {
        using var aiffFile = new TempFile(".aiff");
        using var mp3File = new TempFile(".mp3");

        // say outputs AIFF
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

        // Convert AIFF → MP3 via ffmpeg
        return await ConvertAiffToMp3Async(aiffFile.Path, mp3File.Path);
    }

    private static async Task<byte[]> ConvertAiffToMp3Async(string aiffPath, string mp3Path)
    {
        string? ffmpegPath = null;
        foreach (var c in new[] { "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "ffmpeg" })
        {
            if (c == "ffmpeg" || File.Exists(c)) { ffmpegPath = c; break; }
        }
        if (ffmpegPath == null) throw new InvalidOperationException("ffmpeg not found");

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

    /// <summary>Transcribes audio bytes to text via Gemini multimodal STT, with cache.</summary>
    public async Task<string> SpeechToTextAsync(byte[] audioBytes, string mimeType = "audio/wav")
    {
        // Check STT cache first (keyed by audio content hash)
        var sttCacheKey = ComputeCacheKey(Convert.ToBase64String(audioBytes[..Math.Min(256, audioBytes.Length)]), "stt");
        var sttCachePath = Path.Combine(_cacheDir, $"stt_{sttCacheKey}.txt");
        if (File.Exists(sttCachePath))
        {
            var cachedTranscript = await File.ReadAllTextAsync(sttCachePath);
            _logger.LogInfo($"STT cache hit: {sttCachePath} → \"{cachedTranscript}\"");
            ConsoleUI.PrintPhase($"STT (cached): \"{cachedTranscript[..Math.Min(60, cachedTranscript.Length)]}\"");
            return cachedTranscript;
        }

        var apiKey = _config.GetApiKey();
        var url = $"{GeminiBaseUrl}/{_config.SttModel}:generateContent?key={apiKey}";

        var audioBase64 = Convert.ToBase64String(audioBytes);

        var body = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = "Transkrybuj ten plik audio na tekst w języku polskim. Zwróć TYLKO transkrypcję, bez komentarzy, bez dodatkowego tekstu." },
                        new { inline_data = new { mime_type = mimeType, data = audioBase64 } }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(body);
        _logger.LogInfo($"STT request: mimeType={mimeType}, audioBytes={audioBytes.Length}");
        ConsoleUI.PrintPhase("STT: transcribing operator audio...");

        var response = await _http.PostAsync(url,
            new StringContent(json, Encoding.UTF8, "application/json"));

        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode == 429)
            {
                // Try fallback STT models with progressively lower quotas
                string[] fallbackModels = ["gemini-1.5-flash", "gemini-2.0-flash", "gemini-1.5-flash-8b"];
                foreach (var fallbackModel in fallbackModels)
                {
                    _logger.LogInfo($"Gemini STT quota exhausted for {_config.SttModel} — trying {fallbackModel} fallback");
                    ConsoleUI.PrintPhase($"STT: trying {fallbackModel} fallback...");
                    var fallbackUrl = $"{GeminiBaseUrl}/{fallbackModel}:generateContent?key={_config.GetApiKey()}";
                    var fallbackResponse = await _http.PostAsync(fallbackUrl,
                        new StringContent(json, Encoding.UTF8, "application/json"));
                    var fallbackBody = await fallbackResponse.Content.ReadAsStringAsync();
                    _logger.LogInfo($"STT {fallbackModel} response: HTTP {(int)fallbackResponse.StatusCode}, body={fallbackBody[..Math.Min(200, fallbackBody.Length)]}");

                    if (fallbackResponse.IsSuccessStatusCode)
                    {
                        using var fallbackDoc = JsonDocument.Parse(fallbackBody);
                        var fbTranscript = fallbackDoc.RootElement
                            .GetProperty("candidates")[0]
                            .GetProperty("content")
                            .GetProperty("parts")[0]
                            .GetProperty("text")
                            .GetString()?.Trim() ?? "";
                        _logger.LogInfo($"STT {fallbackModel} transcript: {fbTranscript}");
                        ConsoleUI.PrintConversation("Operator", fbTranscript);
                        if (!string.IsNullOrWhiteSpace(fbTranscript))
                        {
                            await File.WriteAllTextAsync(sttCachePath, fbTranscript);
                            _logger.LogInfo($"STT {fallbackModel} cached to: {sttCachePath}");
                        }
                        return fbTranscript;
                    }
                }
                _logger.LogInfo("All STT fallback models failed — returning empty transcript");
                ConsoleUI.PrintPhase("STT: all models quota exhausted");
                return "";
            }
            var err = $"Gemini STT error {(int)response.StatusCode}: {responseBody}";
            _logger.LogError("STT", err);
            throw new InvalidOperationException(err);
        }

        using var doc = JsonDocument.Parse(responseBody);
        var transcript = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString()
            ?? throw new InvalidOperationException("No text in STT response");

        transcript = transcript.Trim();
        _logger.LogInfo($"STT transcript: {transcript}");
        ConsoleUI.PrintConversation("Operator", transcript);

        // Cache the transcript
        await File.WriteAllTextAsync(sttCachePath, transcript);
        _logger.LogInfo($"STT cached to: {sttCachePath}");

        return transcript;
    }

    /// <summary>Converts WAV bytes to MP3 using ffmpeg.</summary>
    private static async Task<byte[]> ConvertWavToMp3Async(byte[] wavBytes)
    {
        // Try common ffmpeg locations on macOS
        string? ffmpegPath = null;
        foreach (var candidate in new[] { "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "ffmpeg" })
        {
            if (candidate == "ffmpeg" || File.Exists(candidate))
            {
                ffmpegPath = candidate;
                break;
            }
        }
        if (ffmpegPath == null)
            throw new InvalidOperationException("ffmpeg not found. Install via: brew install ffmpeg");

        using var inputFile = new TempFile(".wav");
        using var outputFile = new TempFile(".mp3");

        await File.WriteAllBytesAsync(inputFile.Path, wavBytes);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-y -i \"{inputFile.Path}\" -codec:a libmp3lame -q:a 2 \"{outputFile.Path}\"",
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
            throw new InvalidOperationException($"ffmpeg failed (exit {process.ExitCode}): {stderr[..Math.Min(500, stderr.Length)]}");
        }

        return await File.ReadAllBytesAsync(outputFile.Path);
    }

    /// <summary>Wraps raw PCM audio (24kHz, 16-bit, mono) in a RIFF WAV header.</summary>
    private static byte[] WrapPcmAsWav(byte[] pcmBytes,
        int sampleRate = 24000, short bitsPerSample = 16, short channels = 1)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        int byteRate = sampleRate * channels * bitsPerSample / 8;
        short blockAlign = (short)(channels * bitsPerSample / 8);

        // RIFF chunk
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + pcmBytes.Length);  // file size - 8
        w.Write(Encoding.ASCII.GetBytes("WAVE"));

        // fmt sub-chunk
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);                    // sub-chunk size (PCM)
        w.Write((short)1);              // PCM audio format
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write(blockAlign);
        w.Write(bitsPerSample);

        // data sub-chunk
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(pcmBytes.Length);
        w.Write(pcmBytes);

        return ms.ToArray();
    }
}

/// <summary>Creates a temporary file that is deleted on disposal.</summary>
file sealed class TempFile : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"phonecall_{Guid.NewGuid():N}{System.IO.Path.GetExtension("{ext}")}");

    public TempFile(string ext)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"phonecall_{Guid.NewGuid():N}{ext}");
    }

    public void Dispose()
    {
        try { if (File.Exists(Path)) File.Delete(Path); } catch { }
    }
}
