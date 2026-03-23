using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using EvaluationAgent.Models;
using EvaluationAgent.UI;

namespace EvaluationAgent.Services;

public class OperatorNoteValidator
{
    private readonly IChatClient _chatClient;
    private const int BatchSize = 25;

    // Positive indicators: operator thinks everything is OK
    private static readonly string[] PositiveKeywords =
    [
        "stable", "normal", "within range", "within expected", "no issues", "no problem",
        "looks good", "looks fine", "all good", "all clear", "ok", "okay", "fine",
        "expected range", "expected values", "no anomal", "no fault", "no error",
        "acceptable", "nominal", "correct", "proper", "valid", "verified",
        "consistent", "standard", "regular", "steady", "operating correctly",
        "functioning", "working", "good condition", "no concern", "satisfactory",
        "checks out", "approved", "signed off", "cleared", "routine", "healthy",
        "no deviations", "no irregular", "no warning", "no concerning"
    ];

    // Negative indicators: unambiguous action phrases operators use when they find a real problem.
    // These are specific enough that they can't appear in a negated form like "no X".
    private static readonly string[] NegativeKeywords =
    [
        "flagged it for",
        "flagged for urgent",
        "escalated this",
        "escalated for",
        "ordered an immediate",
        "ordered a",
        "assigned this to",
        "assigned to the troubleshooting",
        "submitted it for root-cause",
        "submitted for root-cause",
        "documented it as a probable fault",
        "documented as a probable fault",
        "triggered a maintenance",
        "scheduled a detailed anomaly",
        "requested a focused technical",
        "opened a deeper diagnostic",
        "marked this case for revalidation",
        "marked for revalidation",
        "investigate immediately",
        "should be investigated immediately",
        "escalated it for",
        "raised for review",
    ];

    public OperatorNoteValidator(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task ValidateAllAsync(List<SensorReading> readings)
    {
        int keywordPositive = 0;
        int keywordNegative = 0;
        int keywordAmbiguous = 0;
        int operatorFalseOk = 0;
        int operatorFalseError = 0;

        // Step A: Keyword pre-filter
        var needsLlm = new List<SensorReading>();
        foreach (var reading in readings)
        {
            var noteClass = ClassifyByKeyword(reading.Data.OperatorNotes);

            if (noteClass == NoteClass.Positive)
            {
                keywordPositive++;
                if (!reading.DataIsValid)
                {
                    reading.Anomalies |= AnomalyType.OperatorFalseOk;
                    operatorFalseOk++;
                }
            }
            else if (noteClass == NoteClass.Negative)
            {
                keywordNegative++;
                if (reading.DataIsValid)
                {
                    reading.Anomalies |= AnomalyType.OperatorFalseError;
                    operatorFalseError++;
                }
            }
            else
            {
                keywordAmbiguous++;
                needsLlm.Add(reading);
            }
        }

        ConsoleUI.PrintInfo($"Keyword classification: {keywordPositive} positive, {keywordNegative} negative, {keywordAmbiguous} ambiguous → LLM");

        // Step B & C: Deduplicate and batch ambiguous notes
        if (needsLlm.Count > 0)
        {
            var (llmFalseOk, llmFalseError) = await ProcessWithLlmAsync(needsLlm);
            operatorFalseOk += llmFalseOk;
            operatorFalseError += llmFalseError;
        }

        ConsoleUI.PrintInfo($"Operator note anomalies: {operatorFalseOk} OperatorFalseOk, {operatorFalseError} OperatorFalseError");
    }

    private async Task<(int falseOk, int falseError)> ProcessWithLlmAsync(List<SensorReading> readings)
    {
        // Deduplicate: note text → list of readings with that note
        var noteGroups = readings
            .GroupBy(r => r.Data.OperatorNotes.Trim())
            .ToDictionary(g => g.Key, g => g.ToList());

        var uniqueNotes = noteGroups.Keys.ToList();
        ConsoleUI.PrintInfo($"Unique ambiguous notes: {uniqueNotes.Count}, sending in batches of {BatchSize}");

        // Cache: note text → classification ("ok" or "error")
        var noteCache = new Dictionary<string, string>(StringComparer.Ordinal);

        int batchCount = 0;
        for (int i = 0; i < uniqueNotes.Count; i += BatchSize)
        {
            var batch = uniqueNotes.Skip(i).Take(BatchSize).ToList();
            batchCount++;

            ConsoleUI.PrintInfo($"LLM batch {batchCount}: classifying {batch.Count} notes...");

            var classifications = await ClassifyBatchAsync(batch);

            for (int j = 0; j < batch.Count && j < classifications.Count; j++)
            {
                noteCache[batch[j]] = classifications[j];
            }

            // For notes where LLM failed to classify, default to "ok"
            foreach (var note in batch.Where(n => !noteCache.ContainsKey(n)))
            {
                noteCache[note] = "ok";
            }
        }

        ConsoleUI.PrintInfo($"LLM classification complete ({batchCount} batches)");

        // Step D: Apply results
        int falseOk = 0;
        int falseError = 0;

        foreach (var (note, readingList) in noteGroups)
        {
            var classification = noteCache.GetValueOrDefault(note, "ok");

            foreach (var reading in readingList)
            {
                if (classification == "ok" && !reading.DataIsValid)
                {
                    reading.Anomalies |= AnomalyType.OperatorFalseOk;
                    falseOk++;
                }
                else if (classification == "error" && reading.DataIsValid)
                {
                    reading.Anomalies |= AnomalyType.OperatorFalseError;
                    falseError++;
                }
            }
        }

        return (falseOk, falseError);
    }

    private async Task<List<string>> ClassifyBatchAsync(List<string> notes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("/no_think");
        sb.AppendLine("You are classifying power plant operator notes. For each note below, determine if the operator is saying the readings are OK/normal (classify as \"ok\") or if the operator is reporting errors/problems (classify as \"error\").");
        sb.AppendLine();
        sb.AppendLine("Reply ONLY with a JSON array of classifications in the same order as the notes, e.g.: [\"ok\",\"error\",\"ok\"]");
        sb.AppendLine("Do not include any other text, just the JSON array.");
        sb.AppendLine();
        sb.AppendLine("Notes:");
        for (int i = 0; i < notes.Count; i++)
        {
            sb.AppendLine($"{i + 1}. \"{notes[i]}\"");
        }

        var prompt = sb.ToString();
        FileLogger.Log("LLM REQUEST", prompt);

        try
        {
            var response = await _chatClient.GetResponseAsync(prompt);
            FileLogger.Log("LLM RESPONSE", response.Text);
            return ParseClassifications(response.Text, notes.Count);
        }
        catch (Exception ex)
        {
            FileLogger.Log("LLM ERROR", ex.ToString());
            ConsoleUI.PrintError($"LLM batch classification failed: {ex.Message}");
            return Enumerable.Repeat("ok", notes.Count).ToList();
        }
    }

    private static List<string> ParseClassifications(string responseText, int expectedCount)
    {
        // Strip thinking tags
        var cleaned = Regex.Replace(responseText, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase).Trim();

        // Find JSON array in response
        var arrayMatch = Regex.Match(cleaned, @"\[[\s\S]*?\]");
        if (!arrayMatch.Success)
        {
            ConsoleUI.PrintInfo($"LLM response didn't contain JSON array, defaulting to 'ok': {cleaned[..Math.Min(100, cleaned.Length)]}");
            return Enumerable.Repeat("ok", expectedCount).ToList();
        }

        try
        {
            var array = JsonSerializer.Deserialize<List<string>>(arrayMatch.Value);
            if (array == null || array.Count == 0)
                return Enumerable.Repeat("ok", expectedCount).ToList();

            // Normalize: ensure only "ok" or "error" values
            var result = array
                .Select(v => v?.Trim().ToLowerInvariant() switch
                {
                    "error" => "error",
                    _ => "ok"
                })
                .ToList();

            // Pad or trim to expected count
            while (result.Count < expectedCount) result.Add("ok");
            return result.Take(expectedCount).ToList();
        }
        catch
        {
            ConsoleUI.PrintInfo("Failed to parse LLM JSON array, defaulting to 'ok'");
            return Enumerable.Repeat("ok", expectedCount).ToList();
        }
    }

    private static NoteClass ClassifyByKeyword(string note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return NoteClass.Positive; // Empty note treated as positive (no complaint)

        var lower = note.ToLowerInvariant();

        bool hasPositive = PositiveKeywords.Any(kw => lower.Contains(kw));
        bool hasNegative = NegativeKeywords.Any(kw => lower.Contains(kw));

        if (hasNegative && !hasPositive) return NoteClass.Negative;
        if (hasPositive && !hasNegative) return NoteClass.Positive;
        if (!hasPositive && !hasNegative) return NoteClass.Ambiguous;

        // Both: ambiguous (conflicting signals)
        return NoteClass.Ambiguous;
    }

    private enum NoteClass { Positive, Negative, Ambiguous }
}
