using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using FailureAgent.UI;

namespace FailureAgent.Tools;

public class LogSearchTools
{
    private readonly string[] _allLines;
    private readonly string[] _filteredLines;
    private readonly List<string> _draft = new();

    private static readonly string[] SeverityKeywords = ["[WARN]", "[ERRO]", "[CRIT]"];
    private static readonly string[] PowerPlantKeywords = [
        "power", "pwr", "cooling", "cool", "pump", "water", "eccs",
        "reactor", "turbine", "generator", "valve", "pressure",
        "temperature", "temp", "fuel", "rod", "steam", "condenser",
        "transformer", "grid", "voltage", "current", "frequency",
        "wtank", "sw_", "cpu", "firmware", "software", "sensor",
        "interlock", "trip", "scram", "shutdown", "alarm", "alert",
        "boiler", "heat", "motor", "circuit", "breaker", "panel",
        "control", "safety", "emergency", "backup", "battery",
        "hydraulic", "pneumatic", "compressor", "fan", "vent",
        "radiation", "containment", "moderator", "neutron"
    ];

    private static readonly Regex ComponentIdRegex = new(
        @"\b([A-Z][A-Z0-9_-]{1,}[0-9]+)\b",
        RegexOptions.Compiled);

    public int TotalLines => _allLines.Length;
    public int FilteredLines => _filteredLines.Length;

    public LogSearchTools(string logFilePath)
    {
        _allLines = File.ReadAllLines(logFilePath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        _filteredLines = _allLines
            .Where(line =>
                SeverityKeywords.Any(s => line.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                PowerPlantKeywords.Any(k => line.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    [Description("Get summary statistics of the log file: total lines, filtered lines count, severity breakdown, unique components found")]
    public string GetLogSummary()
    {
        ConsoleUI.PrintToolCall("GetLogSummary");

        var critCount = _filteredLines.Count(l => l.Contains("[CRIT]", StringComparison.OrdinalIgnoreCase));
        var errCount = _filteredLines.Count(l => l.Contains("[ERRO]", StringComparison.OrdinalIgnoreCase));
        var warnCount = _filteredLines.Count(l => l.Contains("[WARN]", StringComparison.OrdinalIgnoreCase));
        var infoCount = _filteredLines.Count(l => l.Contains("[INFO]", StringComparison.OrdinalIgnoreCase));

        var components = ExtractComponents(_filteredLines);

        var sb = new StringBuilder();
        sb.AppendLine($"Total lines in log: {_allLines.Length}");
        sb.AppendLine($"Filtered relevant lines: {_filteredLines.Length}");
        sb.AppendLine($"Severity breakdown: CRIT={critCount}, ERROR={errCount}, WARN={warnCount}, INFO={infoCount}");
        sb.AppendLine($"Unique components ({components.Count}): {string.Join(", ", components.OrderBy(c => c))}");
        return sb.ToString();
    }

    [Description("Search log file for entries matching a keyword. Returns matching lines (max 50). Searches in pre-filtered relevant lines by default.")]
    public string SearchLogs(
        [Description("Keyword to search for (e.g., 'ECCS', 'pump', 'cooling')")] string keyword,
        [Description("Optional severity filter: WARN, ERROR, CRIT, or ALL. Default: ALL")] string? severity = null,
        [Description("If true, search ALL lines including non-power-plant entries. Default: false")] bool searchAll = false)
    {
        ConsoleUI.PrintToolCall("SearchLogs", $"keyword={keyword}, severity={severity}, searchAll={searchAll}");

        var source = searchAll ? _allLines : _filteredLines;
        var matches = source
            .Where(l => l.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(severity) && !severity.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var severityTag = $"[{severity.ToUpperInvariant()}]";
            matches = matches.Where(l => l.Contains(severityTag, StringComparison.OrdinalIgnoreCase));
        }

        var results = matches.Take(50).ToArray();

        if (results.Length == 0)
            return $"No matches found for '{keyword}'" + (severity != null ? $" with severity {severity}" : "");

        var sb = new StringBuilder();
        sb.AppendLine($"Found {results.Length} matches (showing max 50):");
        foreach (var line in results)
            sb.AppendLine(line);
        return sb.ToString();
    }

    [Description("List all unique component/subsystem IDs found in the filtered log entries")]
    public string ListComponents()
    {
        ConsoleUI.PrintToolCall("ListComponents");

        var components = ExtractComponents(_filteredLines);

        if (components.Count == 0)
            return "No component IDs found in filtered logs.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {components.Count} unique components:");
        foreach (var comp in components.OrderBy(c => c))
        {
            var count = _filteredLines.Count(l => l.Contains(comp, StringComparison.OrdinalIgnoreCase));
            sb.AppendLine($"  {comp} ({count} entries)");
        }
        return sb.ToString();
    }

    [Description("Get all filtered log entries for a specific component ID")]
    public string GetComponentLogs(
        [Description("Component ID like ECCS8, PWR01, WTANK07 etc.")] string componentId)
    {
        ConsoleUI.PrintToolCall("GetComponentLogs", $"componentId={componentId}");

        var matches = _filteredLines
            .Where(l => l.Contains(componentId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
            return $"No entries found for component '{componentId}'. Try SearchLogs with a broader keyword.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {matches.Length} entries for {componentId}:");
        foreach (var line in matches)
            sb.AppendLine(line);
        return sb.ToString();
    }

    [Description("Get all filtered log entries for a specific severity level")]
    public string GetSeverityLogs(
        [Description("Severity: CRIT, ERROR, or WARN")] string severity)
    {
        ConsoleUI.PrintToolCall("GetSeverityLogs", $"severity={severity}");

        var severityTag = $"[{severity.ToUpperInvariant()}]";
        var matches = _filteredLines
            .Where(l => l.Contains(severityTag, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
            return $"No entries found with severity [{severity}].";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {matches.Length} entries with severity [{severity.ToUpperInvariant()}]:");
        foreach (var line in matches)
            sb.AppendLine(line);
        return sb.ToString();
    }

    [Description("Show current condensed log draft entries and estimated token count")]
    public string GetCurrentDraft()
    {
        ConsoleUI.PrintToolCall("GetCurrentDraft");

        if (_draft.Count == 0)
            return "Draft is empty. Use AddLogEntry or AddMultipleEntries to add entries.";

        var text = GetDraftText();
        var tokens = EstimateTokens(text);

        var sb = new StringBuilder();
        sb.AppendLine($"Draft ({_draft.Count} entries, ~{tokens} tokens):");
        for (int i = 0; i < _draft.Count; i++)
            sb.AppendLine($"  [{i + 1}] {_draft[i]}");
        return sb.ToString();
    }

    [Description("Add a condensed log entry to the output draft. Format: [YYYY-MM-DD HH:MM] [SEVERITY] COMPONENT description")]
    public string AddLogEntry(
        [Description("The condensed log line, e.g. '[2026-02-26 06:04] [CRIT] ECCS8 runaway outlet temp. Protection interlock initiated reactor trip.'")] string entry)
    {
        ConsoleUI.PrintToolCall("AddLogEntry", $"entry={entry}");

        _draft.Add(entry.Trim());
        var tokens = EstimateTokens(GetDraftText());
        return $"Added entry #{_draft.Count}. Draft now has {_draft.Count} entries (~{tokens} tokens).";
    }

    [Description("Add multiple condensed log entries at once, separated by newlines")]
    public string AddMultipleEntries(
        [Description("Multiple log lines separated by \\n")] string entries)
    {
        ConsoleUI.PrintToolCall("AddMultipleEntries");

        var lines = entries.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
            _draft.Add(line);

        var tokens = EstimateTokens(GetDraftText());
        return $"Added {lines.Length} entries. Draft now has {_draft.Count} entries (~{tokens} tokens).";
    }

    [Description("Remove a log entry from the draft by its 1-based index number")]
    public string RemoveLogEntry(
        [Description("1-based index of the entry to remove")] int index)
    {
        ConsoleUI.PrintToolCall("RemoveLogEntry", $"index={index}");

        if (index < 1 || index > _draft.Count)
            return $"Invalid index {index}. Draft has {_draft.Count} entries (1-{_draft.Count}).";

        var removed = _draft[index - 1];
        _draft.RemoveAt(index - 1);
        var tokens = EstimateTokens(GetDraftText());
        return $"Removed entry #{index}: '{removed}'. Draft now has {_draft.Count} entries (~{tokens} tokens).";
    }

    [Description("Replace all draft entries with new ones (useful for rewriting the entire draft)")]
    public string ReplaceDraft(
        [Description("All condensed log lines separated by \\n")] string entries)
    {
        ConsoleUI.PrintToolCall("ReplaceDraft");

        _draft.Clear();
        var lines = entries.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _draft.AddRange(lines);

        var tokens = EstimateTokens(GetDraftText());
        return $"Replaced draft with {lines.Length} entries (~{tokens} tokens).";
    }

    [Description("Clear all condensed log entries from the draft")]
    public string ClearDraft()
    {
        ConsoleUI.PrintToolCall("ClearDraft");
        _draft.Clear();
        return "Draft cleared.";
    }

    [Description("Count estimated tokens for the current draft using conservative estimate")]
    public string CountTokens()
    {
        ConsoleUI.PrintToolCall("CountTokens");

        if (_draft.Count == 0)
            return "Draft is empty (0 tokens).";

        var text = GetDraftText();
        var tokens = EstimateTokens(text);
        var charCount = text.Length;
        return $"Draft: {_draft.Count} entries, {charCount} chars, ~{tokens} estimated tokens (limit: 1500).";
    }

    public string GetDraftText() => string.Join("\n", _draft);

    public int GetTokenCount() => EstimateTokens(GetDraftText());

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return (int)Math.Ceiling(text.Length / 3.5);
    }

    private static HashSet<string> ExtractComponents(string[] lines)
    {
        var components = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            foreach (Match match in ComponentIdRegex.Matches(line))
            {
                var id = match.Groups[1].Value;
                if (id.Length >= 3 && !IsCommonFalsePositive(id))
                    components.Add(id);
            }
        }
        return components;
    }

    private static bool IsCommonFalsePositive(string id)
    {
        var upper = id.ToUpperInvariant();
        return upper is "HTTP" or "POST" or "GET" or "PUT" or "DELETE"
            or "JSON" or "XML" or "UTF8" or "ASCII" or "NULL"
            or "TRUE" or "FALSE" or "YYYY" or "INFO" or "WARN"
            or "ERROR" or "CRIT" or "DEBUG" or "TRACE";
    }
}
