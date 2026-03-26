using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Negotiations.Services;

public class ItemMatcherService
{
    private readonly IChatClient _chatClient;
    private readonly CsvDataService _csvData;
    private readonly InteractionLogger _logger;

    // Strip <think>...</think> blocks emitted by Qwen models
    private static readonly Regex ThinkRegex = new(@"<think>[\s\S]*?</think>", RegexOptions.Compiled);

    public ItemMatcherService(IChatClient chatClient, CsvDataService csvData, InteractionLogger logger)
    {
        _chatClient = chatClient;
        _csvData = csvData;
        _logger = logger;
    }

    /// <summary>
    /// Given a natural-language query, returns the matching item code (or null if not found).
    /// Strategy: keyword pre-filter → LLM pick → substring fallback.
    /// </summary>
    public async Task<string?> MatchItemCodeAsync(string query)
    {
        // Step 1: extract keywords (words 3+ chars) from query
        var keywords = ExtractKeywords(query);

        // Step 2: keyword pre-filter — find candidate items
        var candidates = _csvData.FindItemsByKeywords(keywords);

        // Step 3: if exactly one candidate, return it directly
        if (candidates.Count == 1)
        {
            await _logger.LogInfo($"ItemMatcher: exact keyword match '{candidates[0].Name}' for query '{query}'");
            return candidates[0].Code;
        }

        // Step 4: if no candidates, try with any individual word from query
        if (candidates.Count == 0)
        {
            // Try single-word matches
            foreach (var kw in keywords)
            {
                candidates = _csvData.FindItemsByKeywords([kw]);
                if (candidates.Count > 0) break;
            }
        }

        // Step 5: if we have candidates, use LLM to pick the best one
        if (candidates.Count > 0)
        {
            var itemList = string.Join("\n", candidates.Take(100).Select(c => $"- {c.Name}"));
            var prompt = $"""
                Masz listę dostępnych przedmiotów:
                {itemList}

                Użytkownik szuka: "{query}"

                Który przedmiot z listy najlepiej pasuje do zapytania użytkownika?
                Odpowiedz WYŁĄCZNIE dokładną nazwą przedmiotu z listy, bez żadnych dodatkowych słów.
                Jeśli żaden nie pasuje, odpowiedz: BRAK
                """;

            await _logger.LogLlmInteraction("USER (ItemMatcher)", prompt);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.User, prompt)
            };

            var response = await _chatClient.GetResponseAsync(messages);
            var rawText = response.Text ?? "";
            var cleanText = ThinkRegex.Replace(rawText, "").Trim();

            await _logger.LogLlmInteraction("ASSISTANT (ItemMatcher)", cleanText);

            if (cleanText == "BRAK" || string.IsNullOrWhiteSpace(cleanText))
                return null;

            // Validate LLM returned a real item name
            var matchedCode = _csvData.FindItemCodeByName(cleanText.ToLowerInvariant());
            if (matchedCode is not null)
                return matchedCode;

            // Fuzzy fallback: check if LLM output is contained in any candidate name
            var fuzzyMatch = candidates.FirstOrDefault(c =>
                c.Name.Contains(cleanText, StringComparison.OrdinalIgnoreCase) ||
                cleanText.Contains(c.Name, StringComparison.OrdinalIgnoreCase));

            if (fuzzyMatch.Code is not null)
            {
                await _logger.LogInfo($"ItemMatcher: fuzzy match '{fuzzyMatch.Name}' for LLM output '{cleanText}'");
                return fuzzyMatch.Code;
            }
        }

        await _logger.LogInfo($"ItemMatcher: no match found for '{query}'");
        return null;
    }

    private static List<string> ExtractKeywords(string text)
    {
        return Regex.Split(text.ToLowerInvariant(), @"[^\w]+")
            .Where(w => w.Length >= 3)
            .Distinct()
            .ToList();
    }
}
