using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using RadioMonitoring.Models;
using RadioMonitoring.UI;

namespace RadioMonitoring.Services;

/// <summary>
/// Phase 1 pipeline: starts a radio monitoring session, collects all signals,
/// routes them by type (transcription / image / document / noise), and returns
/// a consolidated intelligence report for Phase 2 LLM analysis.
/// </summary>
public class RadioCollector
{
    private readonly CentralaApiClient _centralaApi;
    private readonly IChatClient _visionClient;
    private readonly RunLogger _logger;

    private const int MaxListenCalls = 100;

    public RadioCollector(CentralaApiClient centralaApi, IChatClient visionClient, RunLogger logger)
    {
        _centralaApi = centralaApi;
        _visionClient = visionClient;
        _logger = logger;
    }

    public async Task<string> CollectAsync()
    {
        ConsoleUI.PrintPhase("PHASE 1: Radio Signal Collection");

        var transcriptions = new List<string>();
        var documents = new List<string>();
        var imageDescriptions = new List<string>();

        // Start the listening session
        ConsoleUI.PrintStep("Starting radio monitoring session...");
        var startResponse = await _centralaApi.VerifyAsync(new { action = "start" });
        _logger.LogInfo($"Session started: {startResponse}");
        ConsoleUI.PrintInfo($"Session started: {startResponse}");

        // Listen loop
        bool done = false;
        int listenCount = 0;

        while (!done && listenCount < MaxListenCalls)
        {
            listenCount++;
            ConsoleUI.PrintStep($"Listening... (call {listenCount})");

            var rawResponse = await _centralaApi.VerifyAsync(new { action = "listen" });
            _logger.LogInfo($"Listen #{listenCount} raw response length: {rawResponse.Length}");

            ListenResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<ListenResponse>(rawResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                _logger.LogError("RadioCollector.Deserialize", ex.Message);
                ConsoleUI.PrintError($"Failed to parse listen response: {ex.Message}");
                break;
            }

            if (parsed == null)
            {
                _logger.LogInfo("Listen returned null — stopping");
                done = true;
                break;
            }

            // API signals end of data stream
            if (parsed.Code != 100)
            {
                ConsoleUI.PrintInfo($"Listen complete. Code={parsed.Code}, Message={parsed.Message}");
                _logger.LogInfo($"Listen stream ended. Code={parsed.Code}, Message={parsed.Message}");
                done = true;
                break;
            }

            // Route: text transcription
            if (!string.IsNullOrWhiteSpace(parsed.Transcription))
            {
                var text = parsed.Transcription.Trim();
                if (IsNoise(text))
                {
                    ConsoleUI.PrintInfo($"Skipping noise transcription: {Truncate(text, 80)}");
                    _logger.LogInfo($"Noise transcription skipped: {text}");
                }
                else
                {
                    ConsoleUI.PrintInfo($"Transcription captured: {Truncate(text, 120)}");
                    _logger.LogInfo($"Transcription captured: {text}");
                    transcriptions.Add(text);
                }
                continue;
            }

            // Route: binary attachment
            if (!string.IsNullOrWhiteSpace(parsed.Attachment))
            {
                var meta = parsed.Meta ?? "application/octet-stream";
                ConsoleUI.PrintInfo($"Attachment received: meta={meta}, filesize={parsed.Filesize}");
                _logger.LogInfo($"Attachment: meta={meta}, filesize={parsed.Filesize}");

                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(parsed.Attachment);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Base64Decode", ex.Message);
                    ConsoleUI.PrintError($"Failed to decode Base64 attachment: {ex.Message}");
                    continue;
                }

                if (meta.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    ConsoleUI.PrintStep($"Sending image to Vision model ({meta}, {bytes.Length} bytes)...");
                    var description = await DescribeImageAsync(bytes, meta);
                    ConsoleUI.PrintInfo($"Vision result: {Truncate(description, 200)}");
                    _logger.LogInfo($"Image description: {description}");
                    imageDescriptions.Add(description);
                }
                else if (meta.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                      || meta == "application/json"
                      || meta == "application/pdf")
                {
                    var text = Encoding.UTF8.GetString(bytes);
                    ConsoleUI.PrintInfo($"Document decoded ({meta}): {Truncate(text, 120)}");
                    _logger.LogInfo($"Document: {text}");
                    documents.Add(text);
                }
                else if (meta.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract human-readable strings from audio binary (ID3 tags, embedded text)
                    var extracted = ExtractStringsFromBinary(bytes);
                    if (!string.IsNullOrWhiteSpace(extracted))
                    {
                        ConsoleUI.PrintInfo($"Audio text extracted ({meta}): {Truncate(extracted, 200)}");
                        _logger.LogInfo($"Audio extracted text: {extracted}");
                        documents.Add($"[Audio file metadata/text content]\n{extracted}");
                    }
                    else
                    {
                        ConsoleUI.PrintInfo($"Audio file ({meta}, {bytes.Length} bytes) — no extractable text");
                        _logger.LogInfo($"Audio file with no text: meta={meta}, size={bytes.Length}");
                    }
                }
                else
                {
                    // Try string extraction for unknown binary types too
                    var extracted = ExtractStringsFromBinary(bytes);
                    if (!string.IsNullOrWhiteSpace(extracted))
                    {
                        ConsoleUI.PrintInfo($"Binary text extracted ({meta}): {Truncate(extracted, 200)}");
                        _logger.LogInfo($"Binary extracted text: {extracted}");
                        documents.Add($"[Binary file text content, MIME: {meta}]\n{extracted}");
                    }
                    else
                    {
                        ConsoleUI.PrintInfo($"Unknown binary MIME '{meta}' — no text found, skipping");
                        _logger.LogInfo($"Skipped unknown binary: meta={meta}, size={bytes.Length}");
                    }
                }
                continue;
            }

            // Empty response — may signal end
            ConsoleUI.PrintInfo("Empty listen response — checking if done...");
            _logger.LogInfo("Empty listen response");
        }

        if (listenCount >= MaxListenCalls)
        {
            _logger.LogInfo($"Reached max listen calls ({MaxListenCalls})");
            ConsoleUI.PrintInfo($"Reached maximum listen calls ({MaxListenCalls})");
        }

        return BuildIntelligenceReport(transcriptions, documents, imageDescriptions);
    }

    private async Task<string> DescribeImageAsync(byte[] imageBytes, string mimeType)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, new AIContent[]
            {
                new DataContent(imageBytes, mimeType),
                new TextContent(
                    "Analyze this image carefully. Extract and report ALL of the following if present:\n" +
                    "- City or settlement names\n" +
                    "- Area measurements (square kilometers, hectares, etc.)\n" +
                    "- Number of warehouses, storage facilities, or similar structures\n" +
                    "- Phone numbers or contact information\n" +
                    "- Coordinates or geographic data\n" +
                    "- Any text visible in the image\n" +
                    "Report everything you find. Be precise with numbers.")
            })
        };

        try
        {
            var response = await _visionClient.GetResponseAsync(messages);
            var text = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<TextContent>()
                .Select(t => t.Text)
                .LastOrDefault() ?? "(no description)";
            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError("VisionModel", ex.Message);
            return $"(Vision model error: {ex.Message})";
        }
    }

    private static string BuildIntelligenceReport(
        List<string> transcriptions,
        List<string> documents,
        List<string> imageDescriptions)
    {
        const int MaxReportChars = 6000;

        var sb = new StringBuilder();
        sb.AppendLine("# Intercepted Radio Intelligence Report");
        sb.AppendLine($"## Collection Summary: {transcriptions.Count} transcriptions, {documents.Count} documents, {imageDescriptions.Count} image descriptions");
        sb.AppendLine();

        // Documents first — most structured data
        if (documents.Count > 0)
        {
            sb.AppendLine("## Intercepted Documents / Binary Text");
            for (int i = 0; i < documents.Count; i++)
            {
                sb.AppendLine($"### Document {i + 1}");
                sb.AppendLine(documents[i]);
                sb.AppendLine();
            }
        }

        if (imageDescriptions.Count > 0)
        {
            sb.AppendLine("## Image Analysis Results");
            for (int i = 0; i < imageDescriptions.Count; i++)
            {
                sb.AppendLine($"### Image {i + 1}");
                sb.AppendLine(imageDescriptions[i]);
                sb.AppendLine();
            }
        }

        if (transcriptions.Count > 0)
        {
            sb.AppendLine("## Radio Transcriptions");
            for (int i = 0; i < transcriptions.Count; i++)
            {
                sb.AppendLine($"### Transcription {i + 1}");
                sb.AppendLine(transcriptions[i]);
                sb.AppendLine();
            }
        }

        var report = sb.ToString();

        // Hard cap to prevent LLM context overflow
        if (report.Length > MaxReportChars)
        {
            report = report[..MaxReportChars] + "\n\n[... report truncated to fit LLM context ...]";
        }

        return report;
    }

    /// <summary>
    /// Detects radio noise: very short text, pure random characters, repeated symbols,
    /// or transcriptions dominated by Polish radio static sounds.
    /// </summary>
    private static bool IsNoise(string text)
    {
        if (text.Length < 5) return true;

        // Count actual word characters (letters + digits)
        int wordChars = text.Count(c => char.IsLetterOrDigit(c));
        if (wordChars < 3) return true;

        // If more than 70% of chars are non-alphanumeric, it's likely noise
        double alphaRatio = (double)wordChars / text.Length;
        if (alphaRatio < 0.3) return true;

        // Check for common noise patterns: only one unique character repeated
        var distinct = text.Distinct().Count();
        if (distinct <= 2 && text.Length > 5) return true;

        // Detect Polish radio static transcription patterns
        // If 2+ of these appear, it's radio noise dominateed content
        var noisePatterns = new[] { "ksssh", "kshhh", "kshhhhh", "bzzzzt", "bzzzzzzz", "trzask", "szum", "pisk", "bzzt" };
        var lower = text.ToLowerInvariant();
        int noiseCount = noisePatterns.Count(p => lower.Contains(p));
        if (noiseCount >= 2) return true;

        return false;
    }

    /// <summary>
    /// Extracts human-readable strings from binary data (e.g., ID3 tags in MP3 files).
    /// Only returns strings of 6+ printable ASCII or Polish characters.
    /// </summary>
    private static string ExtractStringsFromBinary(byte[] data)
    {
        var results = new List<string>();
        var current = new StringBuilder();

        foreach (byte b in data)
        {
            char c = (char)b;
            // Accept printable ASCII, tabs, newlines
            if ((b >= 32 && b < 127) || b == 9 || b == 10 || b == 13)
            {
                current.Append(c);
            }
            else
            {
                if (current.Length >= 6)
                    results.Add(current.ToString().Trim());
                current.Clear();
            }
        }
        if (current.Length >= 6)
            results.Add(current.ToString().Trim());

        // Filter out pure technical strings (hex addresses, version strings, etc.)
        var meaningful = results
            .Where(s => s.Length >= 6 && Regex.IsMatch(s, @"[a-zA-Z]{3,}"))
            .Distinct()
            .Take(50)
            .ToList();

        return string.Join('\n', meaningful);
    }

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + "...";
}
