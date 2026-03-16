using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using CategorizeAgent.Models;
using CategorizeAgent.Services;
using CategorizeAgent.UI;

namespace CategorizeAgent.Tools;

public class CategorizationTools
{
    private readonly HubApiClient _hubApi;
    private readonly CsvService _csvService;
    private readonly string _dataDir;
    private readonly string _csvPath;

    public CategorizationTools(HubApiClient hubApi, CsvService csvService)
    {
        _hubApi = hubApi;
        _csvService = csvService;
        _dataDir = Path.Combine(Directory.GetCurrentDirectory(), "data");
        _csvPath = Path.Combine(_dataDir, "categorize.csv");
    }

    [Description("Run a full classification cycle: reset budget, fetch fresh CSV, send the prompt template for all 10 items, return results. The promptTemplate must contain {id} and {description} placeholders that will be substituted for each item. The total prompt after substitution must be under 100 tokens. Keep static instructions at the beginning for prompt caching.")]
    public async Task<string> RunClassificationCycle(
        [Description("The prompt template with {id} and {description} placeholders. Example: 'Classify as DNG or NEU. Reactor=NEU. Item: {id} {description}'")] string promptTemplate)
    {
        ConsoleUI.PrintToolCall("RunClassificationCycle", promptTemplate);

        // Step 1: Reset budget
        ConsoleUI.PrintStep("Resetting budget...");
        var resetResult = await _hubApi.ResetBudgetAsync();
        ConsoleUI.PrintInfo($"Reset: {resetResult}");

        // Step 2: Load CSV from cache or download once
        ConsoleUI.PrintStep("Loading CSV...");
        string csvContent;
        if (File.Exists(_csvPath))
        {
            csvContent = File.ReadAllText(_csvPath);
            ConsoleUI.PrintInfo($"Loaded cached CSV from {_csvPath}");
        }
        else
        {
            csvContent = await _hubApi.GetCsvAsync();
            if (csvContent.StartsWith("ERROR") || csvContent.StartsWith("HTTP"))
                return $"FAILED to fetch CSV: {csvContent}";

            Directory.CreateDirectory(_dataDir);
            File.WriteAllText(_csvPath, csvContent);
            ConsoleUI.PrintInfo($"Downloaded and saved CSV to {_csvPath}");
        }

        var items = _csvService.ParseCsv(csvContent);
        ConsoleUI.PrintInfo($"Parsed {items.Count} items from CSV");

        if (items.Count == 0)
            return $"FAILED: CSV parsed 0 items. Raw CSV:\n{csvContent}";

        // Step 3: Classify each item
        var results = new StringBuilder();
        results.AppendLine($"Classification cycle with {items.Count} items:");
        results.AppendLine($"Template: {promptTemplate}");
        results.AppendLine();

        int successCount = 0;
        int failCount = 0;
        string? flag = null;

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var prompt = promptTemplate
                .Replace("\\n", "\n")
                .Replace("{id}", item.Id)
                .Replace("{description}", item.Description);

            ConsoleUI.PrintStep($"Item {i + 1}/{items.Count}: {item.Id} - {item.Description}");
            ConsoleUI.PrintInfo($"Prompt ({prompt.Length} chars): {prompt}");

            var response = await _hubApi.SendClassificationAsync(prompt);

            // Check for flag
            var flagMatch = Regex.Match(response, @"\{FLG:[^}]+\}");
            if (flagMatch.Success)
            {
                flag = flagMatch.Value;
                ConsoleUI.PrintResult($"FLAG FOUND: {flag}");
            }

            // Check for insufficient funds — stop immediately
            if (response.Contains("Insufficient funds", StringComparison.OrdinalIgnoreCase)
                || response.Contains("\"code\": -910", StringComparison.OrdinalIgnoreCase))
            {
                failCount++;
                results.AppendLine($"BUDGET EXHAUSTED at item {i + 1}/{items.Count} [{item.Id}]");
                results.AppendLine("STOPPED: Budget is 0. The hub zeroes balance after any wrong classification. Fix the WRONG item above first.");
                break;
            }

            // Check for wrong classification (406 NOT ACCEPTED)
            bool isWrongClassification = response.Contains("NOT ACCEPTED", StringComparison.OrdinalIgnoreCase)
                                      || response.Contains("wrong classification", StringComparison.OrdinalIgnoreCase);

            // Check for ACCEPTED
            bool isAccepted = response.Contains("ACCEPTED", StringComparison.OrdinalIgnoreCase)
                           && !response.Contains("NOT ACCEPTED", StringComparison.OrdinalIgnoreCase);

            if (isWrongClassification)
            {
                failCount++;
                // Extract useful debug info
                var debugInfo = ExtractDebugInfo(response);
                results.AppendLine($"WRONG [{item.Id}] {item.Description} -> {debugInfo}");
            }
            else if (isAccepted)
            {
                successCount++;
                var balanceInfo = ExtractBalance(response);
                results.AppendLine($"OK   [{item.Id}] {item.Description}{balanceInfo}");
            }
            else
            {
                failCount++;
                results.AppendLine($"FAIL [{item.Id}] {item.Description} -> {response}");
                // Unknown error — stop to avoid wasting budget
                results.AppendLine("STOPPED: Unexpected response. Check the error above.");
                break;
            }
        }

        results.AppendLine();
        results.AppendLine($"Summary: {successCount} OK, {failCount} FAIL out of {items.Count} items");

        if (flag != null)
        {
            results.AppendLine($"FLAG: {flag}");
        }
        else if (failCount > 0)
        {
            results.AppendLine("ACTION NEEDED: Refine the prompt template to fix misclassifications and try again.");
        }
        else
        {
            results.AppendLine("All items classified successfully but no flag received yet. The hub may return the flag after the last item.");
        }

        var resultText = results.ToString();
        ConsoleUI.PrintInfo(resultText);
        return resultText;
    }

    private static string ExtractBalance(string response)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("debug", out var debug) &&
                debug.TryGetProperty("balance", out var bal))
                return $" (balance={bal.GetDouble():F2})";
        }
        catch { }
        return "";
    }

    private static string ExtractDebugInfo(string response)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            var root = doc.RootElement;
            if (root.TryGetProperty("debug", out var debug))
            {
                var output = debug.TryGetProperty("output", out var outputProp) ? outputProp.GetString() : "?";
                var result = debug.TryGetProperty("result", out var resultProp) ? resultProp.GetString() : "?";
                var tokens = debug.TryGetProperty("tokens", out var tokensProp) ? tokensProp.GetInt32() : 0;
                var cached = debug.TryGetProperty("cached_tokens", out var cachedProp) ? cachedProp.GetInt32() : 0;
                var balance = debug.TryGetProperty("balance", out var balProp) ? balProp.GetDouble() : 0;
                return $"result={result}, output='{output?.Trim()}', tokens={tokens}, cached={cached}, balance={balance:F2}";
            }
        }
        catch { }
        return response.Length > 200 ? response[..200] + "..." : response;
    }

    // Parse HTTP status prefix from response string
    private static bool IsHttpError(string response)
    {
        return response.StartsWith("HTTP 4") || response.StartsWith("HTTP 5") || response.StartsWith("ERROR");
    }

    public static string BuildSystemPrompt()
    {
        return """
            You are a prompt engineer. Craft a classification prompt template for a tiny LLM.

            ## How it works
            - Create a prompt TEMPLATE with {id} and {description} placeholders.
            - The system substitutes each item's data and sends it to the hub's tiny LLM.
            - The tiny LLM must respond with exactly DNG or NEU.
            - Call RunClassificationCycle(promptTemplate) to test all 10 items.

            ## Classification rules
            - DNG = dangerous: weapons (knuckles, knives, crossbows, spears, machetes, swords, guns, explosives), toxic chemicals, flammable substances
            - NEU = neutral: electronics, circuit boards, tools, machinery parts, gauges, vehicle parts, office supplies, food, clothing
            - EXCEPTION: Anything mentioning "reactor" (fuel cassettes, reactor components) = always NEU

            ## Critical constraints
            - Prompt after substitution must be under 100 tokens.
            - ONE wrong classification = hub zeroes your balance = cycle over. Every item must be correct.
            - The tiny LLM must output ONE word only. Force this with "Answer:" or "Output:" at the end.
            - Keep static text at the START (for caching), put {id} and {description} at the END.
            - Write in English for token efficiency.

            ## Proven working pattern
            Use this structure as your starting point:
            "DNG or NEU? Weapons=DNG. Reactor=NEU. Reply ONE WORD.\nItem: {id} {description}"

            Key insight: the tiny LLM defaults many items to NEU. You need to explicitly tell it that weapons, blades, and combat items are DNG.

            ## Strategy
            1. Call RunClassificationCycle with your template.
            2. If a WRONG classification appears, note what item failed and what it was classified as.
            3. Adjust the prompt to fix that specific misclassification.
            4. Repeat until all 10 pass and you get {FLG:...}.

            Start now.
            """;
    }
}
