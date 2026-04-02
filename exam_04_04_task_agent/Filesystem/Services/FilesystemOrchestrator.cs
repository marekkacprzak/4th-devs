using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Filesystem.Models;
using Filesystem.Tools;
using Filesystem.UI;
using Microsoft.Extensions.AI;

namespace Filesystem.Services;

public class FilesystemOrchestrator
{
    private readonly string _notesUrl;
    private readonly FilesystemTools _tools;
    private readonly IChatClient _chatClient;
    private readonly RunLogger _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public FilesystemOrchestrator(FilesystemTools tools, IChatClient chatClient, RunLogger logger, string notesUrl)
    {
        _notesUrl = notesUrl;
        _tools = tools;
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<string> RunAsync()
    {
        // Phase 0: Discovery & Reset
        ConsoleUI.PrintPhase(0, "Discovery & Reset");
        _logger.LogPhase(0, "Discovery & Reset");

        var helpResponse = await _tools.Help();
        _logger.LogInfo($"Help response: {helpResponse}");

        var resetResponse = await _tools.Reset();
        _logger.LogInfo($"Reset response: {resetResponse}");

        // Phase 1: Download & Extract Notes
        ConsoleUI.PrintPhase(1, "Download & Extract Notes");
        _logger.LogPhase(1, "Download & Extract Notes");

        var zipBytes = await _tools.DownloadNotesZip(_notesUrl);
        _logger.LogInfo($"Downloaded {zipBytes.Length} bytes from {_notesUrl}");
        ConsoleUI.PrintInfo($"Downloaded {zipBytes.Length} bytes");

        var notes = ExtractNotesFromZip(zipBytes);
        _logger.LogInfo($"Extracted {notes.Count} files from zip: {string.Join(", ", notes.Keys)}");
        ConsoleUI.PrintInfo($"Extracted {notes.Count} note files: {string.Join(", ", notes.Keys)}");

        // Phase 2: LLM Extraction (cities + people) + C# parsing (goods from transactions)
        ConsoleUI.PrintPhase(2, "LLM Extraction");
        _logger.LogPhase(2, "LLM Extraction");

        var tradeData = await ExtractTradeDataWithLlm(notes);

        // Parse goods directly from transakcje.txt (structured format: "CityA -> good -> CityB")
        var transakcjeContent = notes.FirstOrDefault(n =>
            Path.GetFileName(n.Key).Equals("transakcje.txt", StringComparison.OrdinalIgnoreCase)).Value ?? "";
        var goodsMap = ParseTransakcje(transakcjeContent, tradeData.Cities);
        tradeData.GoodsMap = goodsMap;

        _logger.LogInfo($"Extracted: {tradeData.Cities.Count} cities, {tradeData.People.Count} people, {goodsMap.Count} unique goods");
        ConsoleUI.PrintInfo($"Extracted: {tradeData.Cities.Count} cities, {tradeData.People.Count} people, {goodsMap.Count} unique goods");

        // Sanitize all file names and content as safety net
        SanitizeTradeData(tradeData);

        // Phase 3: Build Filesystem
        ConsoleUI.PrintPhase(3, "Build Filesystem");
        _logger.LogPhase(3, "Build Filesystem");

        var batchOps = BuildBatchOperations(tradeData);
        _logger.LogInfo($"Batch operations: {batchOps.Length} total");
        ConsoleUI.PrintInfo($"Sending {batchOps.Length} operations in batch...");

        var batchResult = await _tools.BatchExecute(batchOps);
        _logger.LogInfo($"Batch result: {batchResult}");

        // Phase 4: Verify & Submit
        ConsoleUI.PrintPhase(4, "Verify & Submit");
        _logger.LogPhase(4, "Verify & Submit");

        var listing = await _tools.ListFiles("/");
        _logger.LogInfo($"Root listing: {listing}");
        ConsoleUI.PrintInfo($"Root listing: {listing}");

        var doneResponse = await _tools.Done();
        _logger.LogInfo($"Done response: {doneResponse}");

        return doneResponse;
    }

    /// <summary>
    /// Parses transakcje.txt lines like "CityA -> good -> CityB" (CityA sells good to CityB).
    /// Returns a dict of good_name -> list of (sellerFileName, sellerDisplayName).
    /// </summary>
    private static Dictionary<string, List<(string FileName, string DisplayName)>> ParseTransakcje(
        string content, List<TradeCity> cities)
    {
        // Build a lookup: ascii_city_name -> (fileName, displayName)
        var cityLookup = new Dictionary<string, (string FileName, string DisplayName)>(StringComparer.OrdinalIgnoreCase);
        foreach (var city in cities)
        {
            cityLookup[city.FileName] = (city.FileName, city.Name);
            // Also map the ASCII version of the display name
            var asciiName = RemovePolishChars(city.Name).ToLowerInvariant();
            if (!cityLookup.ContainsKey(asciiName))
                cityLookup[asciiName] = (city.FileName, city.Name);
        }

        var goodsMap = new Dictionary<string, List<(string FileName, string DisplayName)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Match "CityA -> good -> CityB" (with any arrow variations)
            var match = Regex.Match(line, @"^(.+?)\s*->\s*(.+?)\s*->\s*(.+)$");
            if (!match.Success) continue;

            var sellerRaw = match.Groups[1].Value.Trim();
            var goodRaw = match.Groups[2].Value.Trim();
            // buyer not needed for our purposes

            var sellerAscii = RemovePolishChars(sellerRaw).ToLowerInvariant();
            var goodAscii = RemovePolishChars(goodRaw).ToLowerInvariant();

            if (!cityLookup.TryGetValue(sellerAscii, out var sellerInfo))
            {
                // Try partial match
                sellerInfo = cityLookup.FirstOrDefault(kv =>
                    kv.Key.StartsWith(sellerAscii, StringComparison.OrdinalIgnoreCase) ||
                    sellerAscii.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase)).Value;
            }

            if (sellerInfo == default)
                continue; // skip if can't identify seller

            if (!goodsMap.ContainsKey(goodAscii))
                goodsMap[goodAscii] = new List<(string, string)>();

            // Add seller if not already present
            if (!goodsMap[goodAscii].Any(s => s.FileName == sellerInfo.FileName))
                goodsMap[goodAscii].Add(sellerInfo);
        }

        return goodsMap;
    }

    private static Dictionary<string, string> ExtractNotesFromZip(byte[] zipBytes)
    {
        var notes = new Dictionary<string, string>();
        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            if (entry.Length == 0) continue; // skip directories
            var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
            if (ext is ".txt" or ".md" or "" or ".log")
            {
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                notes[entry.FullName] = reader.ReadToEnd();
            }
        }

        return notes;
    }

    private async Task<TradeData> ExtractTradeDataWithLlm(Dictionary<string, string> notes)
    {
        var notesText = new StringBuilder();
        foreach (var (filename, content) in notes)
        {
            notesText.AppendLine($"=== FILE: {filename} ===");
            notesText.AppendLine(content);
            notesText.AppendLine();
        }

        var systemPrompt = """
            You are a data extraction assistant. You will receive chaotic trade notes written in Polish.
            Extract ONLY cities and people (NOT goods — those are extracted separately).

            Return ONLY valid JSON with this exact schema (no markdown, no explanation, no code blocks):
            {
              "cities": [
                {
                  "name": "City Name (Polish)",
                  "file_name": "city_name_ascii",
                  "needs": {"good1": quantity1, "good2": quantity2}
                }
              ],
              "people": [
                {
                  "first_name": "FirstName",
                  "last_name": "LastName",
                  "city_file": "city_name_ascii",
                  "city_display": "City Name"
                }
              ],
              "goods": []
            }

            STRICT RULES:
            1. file_name, city_file fields: use ONLY ASCII letters, no Polish characters.
               Replace: ą->a, ć->c, ę->e, ł->l, ń->n, ó->o, ś->s, ź->z, ż->z
               Use lowercase and underscores for spaces.
            2. goods names as keys in "needs": singular nominative, no Polish chars, lowercase.
            3. quantities in "needs": numbers only, no units (e.g. 5, not "5 kg").
            4. city_display can keep Polish characters for display purposes only.
            5. PEOPLE — extract ALL 8 people who manage trade in cities.
               Each city has exactly one person. Known people from the notes:
               - Opalino: Iga Kapecka
               - Domatowo: Natan Rams
               - Brudzewo: Rafal Kisiel ("Kisiel" and "Rafal" in notes refer to the same person)
               - Darzlubie: Marta Frantz
               - Celbowo: Oskar Radtke
               - Mechowo: Eliza Redmann
               - Puck: Damian Kroll
               - Karlinkowo: Lena Konkel ("Konkel" is Lena's surname, not a separate person)
               Include ALL 8 people. Do NOT skip any.
            6. "goods" array must be empty [] — goods are extracted separately.
            """;

        var userMessage = $"Extract cities and people from these notes:\n\n{notesText}";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userMessage)
        };

        _logger.LogLlmRequest(1, 3, messages.Count);
        ConsoleUI.PrintLlmRequest(1, 3, messages.Count);

        TradeData? result = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var response = await _chatClient.GetResponseAsync(messages);
            var rawText = response.Text ?? "";
            _logger.LogLlmResponse(rawText);
            ConsoleUI.PrintLlmResponse(rawText.Length > 2000 ? rawText[..2000] + "..." : rawText);

            var cleaned = StripThinkTokens(rawText);
            cleaned = ExtractJsonFromResponse(cleaned);

            try
            {
                result = JsonSerializer.Deserialize<TradeData>(cleaned, JsonOpts);
                if (result != null && result.Cities.Count > 0)
                    break;

                _logger.LogError("LLM", $"Attempt {attempt}: parsed but got empty data, retrying...");
                ConsoleUI.PrintInfo($"Attempt {attempt}: empty data, retrying...");
            }
            catch (Exception ex)
            {
                _logger.LogError("LLM", $"Attempt {attempt}: JSON parse error: {ex.Message}\nRaw: {cleaned}");
                ConsoleUI.PrintError($"Attempt {attempt}: JSON parse error: {ex.Message}");

                if (attempt < 3)
                {
                    messages.Add(new ChatMessage(ChatRole.Assistant, rawText));
                    messages.Add(new ChatMessage(ChatRole.User,
                        "The JSON was invalid. Return ONLY the raw JSON object, no markdown, no explanation."));
                    _logger.LogLlmRequest(attempt + 1, 3, messages.Count);
                    ConsoleUI.PrintLlmRequest(attempt + 1, 3, messages.Count);
                }
            }
        }

        return result ?? new TradeData();
    }

    private static object[] BuildBatchOperations(TradeData data)
    {
        var ops = new List<object>();

        // Create directories
        ops.Add(new { action = "createDirectory", path = "/miasta" });
        ops.Add(new { action = "createDirectory", path = "/osoby" });
        ops.Add(new { action = "createDirectory", path = "/towary" });

        // Create city files: JSON with goods needed
        foreach (var city in data.Cities)
        {
            var needsJson = JsonSerializer.Serialize(city.Needs, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            ops.Add(new { action = "createFile", path = $"/miasta/{city.FileName}", content = needsJson });
        }

        // Create person files: name + markdown link to city
        // File names must be lowercase ASCII only (^[a-z0-9_]+$)
        var seenPeople = new HashSet<string>();
        foreach (var person in data.People)
        {
            var fileName = $"{RemovePolishChars(person.FirstName)}_{RemovePolishChars(person.LastName)}".ToLowerInvariant();
            if (!seenPeople.Add(fileName)) continue; // skip duplicates
            var content = $"{person.FirstName} {person.LastName}\n[{person.CityDisplayName}](../miasta/{person.CityFileName})";
            ops.Add(new { action = "createFile", path = $"/osoby/{fileName}", content });
        }

        // Create goods files: all seller cities as markdown links (one per line)
        foreach (var (goodName, sellers) in data.GoodsMap)
        {
            var links = string.Join("\n", sellers.Select(s =>
                $"[{s.DisplayName}](../miasta/{s.FileName})"));
            ops.Add(new { action = "createFile", path = $"/towary/{goodName}", content = links });
        }

        return ops.ToArray();
    }

    private static void SanitizeTradeData(TradeData data)
    {
        foreach (var city in data.Cities)
        {
            city.FileName = RemovePolishChars(city.FileName).ToLowerInvariant().Replace(' ', '_');
            var sanitizedNeeds = new Dictionary<string, int>();
            foreach (var (k, v) in city.Needs)
                sanitizedNeeds[RemovePolishChars(k).ToLowerInvariant()] = v;
            city.Needs = sanitizedNeeds;
        }

        foreach (var person in data.People)
        {
            person.CityFileName = RemovePolishChars(person.CityFileName).ToLowerInvariant().Replace(' ', '_');
            person.FirstName = person.FirstName.Trim();
            person.LastName = person.LastName.Trim();
        }
    }

    private static string RemovePolishChars(string s)
    {
        return s
            .Replace('ą', 'a').Replace('Ą', 'A')
            .Replace('ć', 'c').Replace('Ć', 'C')
            .Replace('ę', 'e').Replace('Ę', 'E')
            .Replace('ł', 'l').Replace('Ł', 'L')
            .Replace('ń', 'n').Replace('Ń', 'N')
            .Replace('ó', 'o').Replace('Ó', 'O')
            .Replace('ś', 's').Replace('Ś', 'S')
            .Replace('ź', 'z').Replace('Ź', 'Z')
            .Replace('ż', 'z').Replace('Ż', 'Z');
    }

    private static string StripThinkTokens(string text)
    {
        return Regex.Replace(text, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase).Trim();
    }

    private static string ExtractJsonFromResponse(string text)
    {
        // Strip markdown code fences if present
        var fenceMatch = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)```");
        if (fenceMatch.Success)
            return fenceMatch.Groups[1].Value.Trim();

        // Find first { and last }
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
            return text[start..(end + 1)];

        return text;
    }
}
