using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Phonecall2.UI;

namespace Phonecall2.Services;

/// <summary>
/// Orchestrates a scripted 3-step phone conversation with the operator.
///
/// Flow:
///   STEP 0: Start session (action: "start")
///   STEP 1: Introduce as Tymon Gajewski
///   STEP 2: Ask about road status for RD224, RD472, RD820 (mention Zygfryd transport)
///   STEP 3: Request monitoring deactivation on passable roads
///
/// Password "BARBAKAN" is sent whenever the operator asks for it.
/// Uses local LLM (LM Studio) once — to extract passable road IDs from the operator's transcript.
/// </summary>
public class ConversationOrchestrator
{
    private readonly CentralaApiClient _centrala;
    private readonly LocalAudioService _audio;
    private readonly IChatClient _chatClient;
    private readonly RunLogger _logger;
    private const int MaxAttempts = 3;

    // Known passable road ID candidates
    private static readonly string[] KnownRoads = ["RD224", "RD472", "RD820"];

    // Keywords that indicate the operator is asking for a password
    private static readonly string[] PasswordKeywords =
        ["hasł", "haslo", "autoryzac", "podaj hasło", "weryfikac", "kod dostępu", "kod dostepu", "BARBAKAN"];

    public ConversationOrchestrator(
        CentralaApiClient centrala,
        LocalAudioService audio,
        IChatClient chatClient,
        RunLogger logger)
    {
        _centrala = centrala;
        _audio = audio;
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<string> RunConversationAsync()
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ConsoleUI.PrintPhase($"Conversation attempt {attempt}/{MaxAttempts}");
            _logger.LogInfo($"=== Conversation attempt {attempt}/{MaxAttempts} ===");

            try
            {
                return await ExecuteConversationAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError("Conversation", $"Attempt {attempt} failed: {ex.GetType().Name}: {ex.Message}");
                ConsoleUI.PrintError($"Attempt {attempt} failed: {ex.Message}. Restarting...");

                if (attempt < MaxAttempts)
                    await Task.Delay(3000);
            }
        }

        return "ERROR: All conversation attempts failed.";
    }

    private async Task<string> ExecuteConversationAsync()
    {
        // ── STEP 0: Start session ──────────────────────────────────────────────
        ConsoleUI.PrintPhase("STEP 0: Starting session");
        var startResponse = await _centrala.StartSessionAsync();
        _logger.LogInfo($"Start response: {startResponse}");

        // Handle possible audio in the start response
        var (startAudio, startMime) = TryExtractAudio(startResponse);
        if (startAudio != null)
        {
            var startTranscript = await _audio.SpeechToTextAsync(startAudio, startMime);
            _logger.LogInfo($"Operator greeting: {startTranscript}");
            if (NeedsPassword(startTranscript))
            {
                startResponse = await SendPasswordAsync();
            }
        }

        // ── STEP 1: Introduce as Tymon Gajewski ───────────────────────────────
        ConsoleUI.PrintPhase("STEP 1: Introduction");
        const string introText = "Dzień dobry, nazywam się Tymon Gajewski.";
        ConsoleUI.PrintConversation("Tymon", introText);

        var step1Response = await SendAndReceiveAsync(introText);
        _logger.LogInfo($"After intro — operator: {step1Response.OperatorText}");

        if (NeedsPassword(step1Response.OperatorText))
        {
            var pwdResp = await SendPasswordTurnAsync();
            step1Response = pwdResp;
        }

        // ── STEP 2: Ask about road status ─────────────────────────────────────
        // After "W jakiej sprawie dzwonisz?", identify purpose + send BARBAKAN password
        // + ask about road status in one turn.
        ConsoleUI.PrintPhase("STEP 2: Road status query");
        const string roadQueryText =
            "Hasło barbakan. Dzwonię w sprawie dostarczenia tajnego transportu dla Zygfryda. " +
            "Potrzebuję znaleźć przejezdną drogę, aby dostarczyć ten transport. " +
            "Proszę o informację o stanie dróg RD224, RD472 i RD820.";
        ConsoleUI.PrintConversation("Tymon", roadQueryText);

        var step2Response = await SendAndReceiveAsync(roadQueryText);
        _logger.LogInfo($"Operator road status: {step2Response.OperatorText}");

        // Handle error code in STEP 2 (e.g. -800/-810 "suspicious talking")
        // Retry up to 2 times since the server evaluation is non-deterministic.
        var step2Code = TryExtractCode(step2Response.RawApiResponse);
        for (int step2Retry = 0; step2Code < 0 && step2Retry < 2; step2Retry++)
        {
            var hint = TryExtractHint(step2Response.RawApiResponse);
            _logger.LogInfo($"STEP 2 error code {step2Code} (retry {step2Retry + 1}/2). Hint: {hint}. Operator: '{step2Response.OperatorText}'");

            if (NeedsPassword(step2Response.OperatorText))
            {
                _logger.LogInfo("Password required at STEP 2 — sending BARBAKAN");
                var pwdResponse = await SendPasswordTurnAsync();
                _logger.LogInfo($"After password in STEP 2: {pwdResponse.OperatorText}");
            }

            // Retry road query
            await Task.Delay(2000);
            step2Response = await SendAndReceiveAsync(roadQueryText);
            step2Code = TryExtractCode(step2Response.RawApiResponse) ?? 0;
            _logger.LogInfo($"STEP 2 retry {step2Retry + 1} result: code={step2Code}, transcript='{step2Response.OperatorText}'");
        }

        if ((step2Code = TryExtractCode(step2Response.RawApiResponse) ?? step2Code) < 0)
        {
            throw new ConversationFailedException(
                $"Operator rejected STEP 2 with code {step2Code}: '{step2Response.OperatorText}'");
        }

        if (NeedsPassword(step2Response.OperatorText))
        {
            var pwdResponse = await SendPasswordTurnAsync();
            step2Response = step2Response with { OperatorText = pwdResponse.OperatorText };
        }

        // Extract passable roads from operator's response
        var passableRoads = await ExtractPassableRoadsAsync(step2Response.OperatorText);
        _logger.LogInfo($"Passable roads identified: {string.Join(", ", passableRoads)}");
        ConsoleUI.PrintInfo($"Passable roads: {string.Join(", ", passableRoads)}");

        if (passableRoads.Count == 0)
            throw new ConversationFailedException($"Could not determine passable roads from: \"{step2Response.OperatorText}\"");

        // ── STEP 3: Request monitoring deactivation ───────────────────────────
        ConsoleUI.PrintPhase("STEP 3: Request monitoring deactivation");
        var roadPhrase = passableRoads.Count == 1
            ? $"drodze {passableRoads[0]}"
            : $"drogach {string.Join(" i ", passableRoads)}";

        var deactivateText =
            $"Proszę o wyłączenie monitoringu na {roadPhrase}. " +
            $"To jest tajna operacja zarządzona przez Zygfryda. " +
            $"Działam na jego rozkaz i potrzebuję tej drogi do przeprowadzenia operacji.";
        ConsoleUI.PrintConversation("Tymon", deactivateText);

        var step3Response = await SendAndReceiveAsync(deactivateText);
        _logger.LogInfo($"Operator after deactivation request: {step3Response.OperatorText}");

        if (NeedsPassword(step3Response.OperatorText))
        {
            step3Response = await SendPasswordTurnAsync();
        }

        // Check for password requirement in STEP 3 response.
        // Code 160 = "Password required." — operator needs BARBAKAN.
        var step3Code = TryExtractCode(step3Response.RawApiResponse);
        var step3Message = TryExtractMessage(step3Response.RawApiResponse) ?? "";

        bool step3NeedsPassword = step3Code == 160
            || NeedsPassword(step3Response.OperatorText)
            || NeedsPassword(step3Message);

        if (step3NeedsPassword)
        {
            _logger.LogInfo($"STEP 3 password required (code={step3Code}, message='{step3Message}') — sending BARBAKAN");
            step3Response = await SendPasswordTurnAsync();
            step3Code = TryExtractCode(step3Response.RawApiResponse) ?? step3Code;
            _logger.LogInfo($"After password in STEP 3: code={step3Code}, operator='{step3Response.OperatorText}'");
        }
        else if (step3Code is < 0)
        {
            var hint = TryExtractHint(step3Response.RawApiResponse);
            _logger.LogInfo($"STEP 3 error code {step3Code}. Hint: {hint}. Operator: '{step3Response.OperatorText}'");
            throw new ConversationFailedException(
                $"STEP 3 rejected (code {step3Code}). Hint: {hint}. Operator: '{step3Response.OperatorText}'");
        }

        // Extract flag from the final API response (JSON or transcript)
        var flag = TryExtractFlag(step3Response.RawApiResponse)
                ?? TryExtractFlag(step3Response.OperatorText);
        if (flag != null)
        {
            ConsoleUI.PrintResult($"FLAG: {flag}");
            return flag;
        }

        _logger.LogInfo($"Final operator: '{step3Response.OperatorText}', raw: {step3Response.RawApiResponse[..Math.Min(200, step3Response.RawApiResponse.Length)]}");

        // Return the full response for manual inspection
        return $"Conversation completed. Final response: {step3Response.RawApiResponse}";
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a text message (via TTS) and receives the operator's response (STT transcribed).
    /// Always transcribes audio from the response — even error responses (e.g. code -800).
    /// </summary>
    private async Task<(string OperatorText, string RawApiResponse)> SendAndReceiveAsync(string text)
    {
        var wavBytes = await _audio.TextToSpeechAsync(text);
        var base64 = Convert.ToBase64String(wavBytes);

        var rawResponse = await _centrala.SendAudioAsync(base64);

        var (audioBytes, mimeType) = TryExtractAudio(rawResponse);
        if (audioBytes == null || audioBytes.Length == 0)
        {
            _logger.LogInfo($"No audio in response, raw: {rawResponse[..Math.Min(300, rawResponse.Length)]}");
            return ("", rawResponse);
        }

        var transcript = await _audio.SpeechToTextAsync(audioBytes, mimeType);

        // Check for error codes and log them
        var code = TryExtractCode(rawResponse);
        _logger.LogInfo($"Response code={code}, transcript: {transcript}");

        return (transcript, rawResponse);
    }

    private async Task<string> SendPasswordAsync()
    {
        ConsoleUI.PrintPhase("Sending password: BARBAKAN");
        const string passwordText = "Moje hasło to barbakan, powtarzam, barbakan.";
        ConsoleUI.PrintConversation("Tymon", passwordText);
        var mp3 = await _audio.TextToSpeechAsync(passwordText);
        var base64 = Convert.ToBase64String(mp3);
        var response = await _centrala.SendAudioAsync(base64);
        _logger.LogInfo($"Password response: {response}");
        return response;
    }

    private async Task<(string OperatorText, string RawApiResponse)> SendPasswordTurnAsync()
    {
        ConsoleUI.PrintPhase("Sending password: BARBAKAN");
        const string passwordText = "Moje hasło to barbakan, powtarzam, barbakan.";
        ConsoleUI.PrintConversation("Tymon", passwordText);
        return await SendAndReceiveAsync(passwordText);
    }

    /// <summary>
    /// Uses local LLM to extract passable road IDs from operator transcript.
    /// Falls back to regex if LLM fails or returns garbage.
    /// </summary>
    private async Task<List<string>> ExtractPassableRoadsAsync(string transcript)
    {
        _logger.LogInfo($"Extracting passable roads from: {transcript}");
        if (string.IsNullOrWhiteSpace(transcript))
        {
            _logger.LogInfo("Empty transcript — cannot extract roads (STT failed)");
            return [];
        }

        // First try fast regex extraction
        var regexRoads = ExtractRoadsWithRegex(transcript);
        if (regexRoads.Count > 0)
        {
            _logger.LogInfo($"Regex found roads: {string.Join(", ", regexRoads)}");
            return regexRoads;
        }

        // Fallback to LLM
        try
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System,
                    """
                    Na podstawie transkrypcji odpowiedzi operatora wymień TYLKO identyfikatory dróg
                    (spośród: RD224, RD472, RD820) które są przejezdne, bezpieczne lub można nimi przejechać.
                    Odpowiedz WYŁĄCZNIE identyfikatorami oddzielonymi przecinkami, np.: RD224, RD820
                    Jeśli żadna droga nie jest przejezdna lub nie masz pewności, odpowiedz: BRAK
                    Nie dodawaj żadnego dodatkowego tekstu.
                    """),
                new(ChatRole.User, transcript)
            };

            _logger.LogInfo("Calling local LLM for road extraction...");
            var response = await _chatClient.GetResponseAsync(messages);
            var raw = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<Microsoft.Extensions.AI.TextContent>()
                .Select(t => t.Text)
                .LastOrDefault() ?? "";

            // Strip think tokens
            raw = Regex.Replace(raw, @"<think>[\s\S]*?</think>", "").Trim();
            _logger.LogInfo($"LLM road extraction response: {raw}");

            if (raw.Contains("BRAK", StringComparison.OrdinalIgnoreCase))
                return [];

            var llmRoads = Regex.Matches(raw, @"RD\d{3}")
                .Select(m => m.Value.ToUpper())
                .Where(r => KnownRoads.Contains(r))
                .Distinct()
                .ToList();

            if (llmRoads.Count > 0) return llmRoads;
        }
        catch (Exception ex)
        {
            _logger.LogError("RoadExtraction", $"LLM call failed: {ex.Message}");
        }

        return [];
    }

    /// <summary>
    /// Extracts road IDs from transcript by looking for positive context words near road identifiers.
    /// Handles common Polish responses like "RD224 jest przejezdna", "droga RD472 - OK", etc.
    /// </summary>
    private static List<string> ExtractRoadsWithRegex(string transcript)
    {
        var result = new List<string>();
        var lower = transcript.ToLowerInvariant();

        // Positive words indicating a road is passable
        string[] positiveWords = ["przejezdna", "przejezdny", "bezpieczna", "bezpieczny",
            "ok", "wolna", "wolny", "dostępna", "dostępny", "możliwa", "można",
            "nie ma przeszkód", "brak przeszkód", "czysta", "czysty"];

        // Negative words indicating a road is blocked
        string[] negativeWords = ["zablokowana", "zablokowany", "nieprzejezdna", "nieprzejezdny",
            "niebezpieczna", "niebezpieczny", "zamknięta", "zamknięty", "skażona",
            "uszkodzona", "brak przejazdu"];

        foreach (var road in KnownRoads)
        {
            var roadLower = road.ToLowerInvariant();
            var roadIdx = lower.IndexOf(roadLower, StringComparison.Ordinal);
            if (roadIdx < 0) continue;

            // Look at context window around the road ID (±150 chars)
            var start = Math.Max(0, roadIdx - 150);
            var end = Math.Min(lower.Length, roadIdx + 150);
            var context = lower[start..end];

            var hasPositive = positiveWords.Any(w => context.Contains(w, StringComparison.Ordinal));
            var hasNegative = negativeWords.Any(w => context.Contains(w, StringComparison.Ordinal));

            if (hasPositive && !hasNegative)
                result.Add(road);
        }

        return result;
    }

    /// <summary>
    /// Checks if the operator transcript contains a password request.
    /// </summary>
    private static bool NeedsPassword(string operatorText)
    {
        if (string.IsNullOrWhiteSpace(operatorText)) return false;
        var lower = operatorText.ToLowerInvariant();
        return PasswordKeywords.Any(kw => lower.Contains(kw.ToLowerInvariant(), StringComparison.Ordinal));
    }

    /// <summary>
    /// Parses operator audio bytes and detected mime type from raw JSON response.
    /// Tries multiple JSON paths to find base64 audio.
    /// </summary>
    private (byte[]? Bytes, string MimeType) TryExtractAudio(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return (null, "audio/mpeg");

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            string? base64 = null;

            // Path: { "audio": "<base64>" }
            if (root.TryGetProperty("audio", out var audioRoot))
                base64 = audioRoot.GetString();

            // Path: { "answer": { "audio": "<base64>" } }
            if (base64 == null && root.TryGetProperty("answer", out var answer))
            {
                if (answer.TryGetProperty("audio", out var audio))
                    base64 = audio.GetString();
            }

            // Path: { "message": "<base64>" } (only if it looks like base64)
            if (base64 == null && root.TryGetProperty("message", out var msg))
            {
                var msgStr = msg.GetString();
                if (IsLikelyBase64(msgStr))
                    base64 = msgStr;
            }

            if (base64 == null) return (null, "audio/mpeg");

            var bytes = Convert.FromBase64String(base64);
            var mimeType = DetectAudioMimeType(bytes);
            _logger.LogInfo($"Extracted operator audio: {bytes.Length} bytes, detected={mimeType}");
            return (bytes, mimeType);
        }
        catch (Exception ex)
        {
            _logger.LogError("AudioExtract", $"Failed to extract audio from response: {ex.Message}\nRaw: {rawJson[..Math.Min(500, rawJson.Length)]}");
            return (null, "audio/mpeg");
        }
    }

    /// <summary>Detects audio mime type from magic bytes.</summary>
    private static string DetectAudioMimeType(byte[] bytes)
    {
        if (bytes.Length < 4) return "audio/mpeg";
        // MP3 with ID3 tag: 49 44 33 ("ID3")
        if (bytes[0] == 0x49 && bytes[1] == 0x44 && bytes[2] == 0x33) return "audio/mpeg";
        // MP3 frame sync: FF FB / FF F3 / FF F2
        if (bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0) return "audio/mpeg";
        // WAV: 52 49 46 46 ("RIFF")
        if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46) return "audio/wav";
        // OGG: 4F 67 67 53 ("OggS")
        if (bytes[0] == 0x4F && bytes[1] == 0x67 && bytes[2] == 0x67 && bytes[3] == 0x53) return "audio/ogg";
        return "audio/mpeg"; // default
    }

    /// <summary>Looks for a flag pattern ({{...}}) in the raw API response.</summary>
    private static string? TryExtractFlag(string rawJson)
    {
        var match = Regex.Match(rawJson, @"\{\{[^}]+\}\}");
        if (match.Success) return match.Value;

        // Also check for "flag" JSON property
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("flag", out var flagProp))
                return flagProp.GetString();
            if (doc.RootElement.TryGetProperty("message", out var msgProp))
            {
                var msg = msgProp.GetString() ?? "";
                var flagMatch = Regex.Match(msg, @"\{\{[^}]+\}\}");
                if (flagMatch.Success) return flagMatch.Value;
            }
        }
        catch { }

        return null;
    }

    /// <summary>Extracts the "message" field from a JSON response if present.</summary>
    private static string? TryExtractMessage(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("message", out var msgProp))
                return msgProp.GetString();
        }
        catch { }
        return null;
    }

    /// <summary>Extracts the "hint" field from a JSON response if present.</summary>
    private static string? TryExtractHint(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("hint", out var hintProp))
                return hintProp.GetString();
        }
        catch { }
        return null;
    }

    /// <summary>Extracts the numeric "code" field from a JSON response, or null.</summary>
    private static int? TryExtractCode(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("code", out var codeProp) &&
                codeProp.TryGetInt32(out int code))
                return code;
        }
        catch { }
        return null;
    }

    private static bool IsLikelyBase64(string? s)
    {
        if (string.IsNullOrEmpty(s) || s.Length < 100) return false;
        // Base64 chars only + length divisible by 4 (with padding)
        return Regex.IsMatch(s, @"^[A-Za-z0-9+/=]+$");
    }
}

public class ConversationFailedException : Exception
{
    public ConversationFailedException(string message) : base(message) { }
}
