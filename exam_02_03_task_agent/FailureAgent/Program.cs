using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using FailureAgent.Adapters;
using FailureAgent.Config;
using FailureAgent.Services;
using FailureAgent.Telemetry;
using FailureAgent.UI;

// 1. Load configuration

// Load .env file so its values override appsettings.json via AddEnvironmentVariables()
var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envPath))
{
    foreach (var line in File.ReadAllLines(envPath))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            continue;
        var sep = trimmed.IndexOf('=');
        if (sep > 0)
            Environment.SetEnvironmentVariable(trimmed[..sep], trimmed[(sep + 1)..]);
    }
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var agentConfig = new AgentConfig();
configuration.GetSection("Agent").Bind(agentConfig);

var hubConfig = new HubConfig();
configuration.GetSection("Hub").Bind(hubConfig);

var telemetryConfig = new TelemetryConfig();
configuration.GetSection("Telemetry").Bind(telemetryConfig);

using var telemetry = new TelemetrySetup(telemetryConfig);

// 2. Create services
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
var hubApi = new HubApiClient(httpClient, hubConfig);
var logDownloader = new LogDownloader(httpClient, hubConfig.DataBaseUrl, hubConfig.ApiKey);
var chatClient = OpenAiClientFactory.CreateChatClient(agentConfig, telemetryConfig);

// 3. Download log file
ConsoleUI.PrintBanner("FAILURE", "Power plant failure log analysis");

var dataDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "data"));
Directory.CreateDirectory(dataDir);

var aiLogPath = Path.Combine(dataDir, "ai_requests.log");
File.WriteAllText(aiLogPath, $"=== AI Log Session {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n\n");
ConsoleUI.PrintInfo($"AI request/response log: {aiLogPath}");

async Task<ChatResponse> LoggedGetResponseAsync(IChatClient client, string prompt, string label)
{
    File.AppendAllText(aiLogPath, $"--- [{label}] REQUEST @ {DateTime.Now:HH:mm:ss} ---\n{prompt}\n\n");
    var response = await client.GetResponseAsync(prompt);
    File.AppendAllText(aiLogPath, $"--- [{label}] RESPONSE @ {DateTime.Now:HH:mm:ss} ---\n{response.Text}\n\n");
    return response;
}

var logFilePath = Path.Combine(dataDir, "failure.log");
if (!File.Exists(logFilePath))
{
    ConsoleUI.PrintInfo("Downloading failure.log...");
    logFilePath = await logDownloader.DownloadLogFileAsync(dataDir);
}
else
{
    var cachedUrl = $"{hubConfig.DataBaseUrl}/{hubConfig.ApiKey}/failure.log";
    ConsoleUI.PrintInfo($"Using cached log file: {logFilePath} (from: {cachedUrl})");
}

// 4. Read and categorize logs
var allLines = File.ReadAllLines(logFilePath)
    .Where(l => !string.IsNullOrWhiteSpace(l))
    .ToArray();

ConsoleUI.PrintInfo($"Total lines: {allLines.Length}");

var critLines = allLines.Where(l => l.Contains("[CRIT]")).ToArray();
var erroLines = allLines.Where(l => l.Contains("[ERRO]")).ToArray();
var warnLines = allLines.Where(l => l.Contains("[WARN]")).ToArray();

ConsoleUI.PrintInfo($"CRIT: {critLines.Length}, ERRO: {erroLines.Length}, WARN: {warnLines.Length}");

// 5. Extract component ID from anywhere in the line
// Match IDs like ECCS8, WTANK07, STMTURB12, WTRPMP, PWR01, WSTPOOL2, and also FIRMWARE (no trailing digit)
var componentRegex = new Regex(@"\b(FIRMWARE|[A-Z][A-Z0-9_-]*[0-9]+)\b", RegexOptions.Compiled);

string ExtractComponent(string line)
{
    // Skip the timestamp and severity prefix, search the message portion
    var bracketEnd = line.LastIndexOf("] ");
    var searchArea = bracketEnd >= 0 ? line[(bracketEnd + 2)..] : line;
    var match = componentRegex.Match(searchArea);
    return match.Success ? match.Groups[1].Value : "UNKNOWN";
}

string GetMessageSignature(string line)
{
    // Extract component + first ~60 chars of message for grouping
    var component = ExtractComponent(line);
    var bracketEnd = line.LastIndexOf("] ");
    var msg = bracketEnd >= 0 ? line[(bracketEnd + 2)..].Trim() : line;
    // Normalize: remove the component from the message to get pure message template
    msg = msg.Replace(component, "").Trim();
    // Take first 80 chars as signature
    if (msg.Length > 80) msg = msg[..80];
    return $"{component}|{msg}";
}

// Deduplicate: keep first occurrence of each unique message signature
List<string> Deduplicate(string[] lines)
{
    var seen = new HashSet<string>();
    var result = new List<string>();
    foreach (var line in lines)
    {
        var key = GetMessageSignature(line);
        if (seen.Add(key))
            result.Add(line);
    }
    return result;
}

var dedupedCrit = Deduplicate(critLines);
var dedupedErro = Deduplicate(erroLines);
var dedupedWarn = Deduplicate(warnLines);

ConsoleUI.PrintInfo($"After dedup - CRIT: {dedupedCrit.Count}, ERRO: {dedupedErro.Count}, WARN: {dedupedWarn.Count}");

// 6. Build source logs for LLM (CRIT + ERRO merged chronologically)
var allCritErro = dedupedCrit.Concat(dedupedErro)
    .OrderBy(l => l) // timestamps are at start, so lexicographic = chronological
    .ToList();

// Also prepare all events (including WARN) for improvement rounds
var allDeduped = dedupedCrit.Concat(dedupedErro).Concat(dedupedWarn)
    .OrderBy(l => l)
    .ToList();

var critErroLogs = new StringBuilder();
critErroLogs.AppendLine("ALL CRITICAL AND ERROR EVENTS (chronological order):");
foreach (var line in allCritErro) critErroLogs.AppendLine(line);

ConsoleUI.PrintInfo($"CRIT+ERRO for LLM: {critErroLogs.Length} chars (~{critErroLogs.Length / 4} tokens)");

// 7. Use LLM to condense CRIT+ERRO entries
var condensationPrompt = $"""
    /no_think
    You are condensing power plant failure logs for technician review.
    Below are deduplicated CRITICAL and ERROR entries from a power plant incident.

    TASK: Produce condensed log output. Rules:
    - Output ONLY log lines, no commentary/headers/explanations
    - One event per line
    - Format: [YYYY-MM-DD HH:MM] [SEVERITY] COMPONENT_ID short_description
    - CRITICAL: Output ALL lines in STRICT CHRONOLOGICAL ORDER by timestamp. Do NOT group by severity.
    - Keep ALL unique CRIT events
    - Keep ALL unique ERRO events
    - Preserve key identifiers like SAFETY_CHECK=pass verbatim
    - Shorten descriptions but keep technical details needed for diagnosis
    - Remove seconds from timestamps
    - Total must be under 4500 characters

    {critErroLogs}
    """;

ConsoleUI.PrintInfo("Sending to LLM for condensation...");
var condensed = await LoggedGetResponseAsync(chatClient, condensationPrompt, "CONDENSATION");
var condensedText = CleanLlmOutput(condensed.Text);

var estimatedTokens = EstimateTokens(condensedText);
var lineCount = condensedText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
ConsoleUI.PrintInfo($"Condensed: {lineCount} lines, {condensedText.Length} chars, ~{estimatedTokens} tokens");

// 8. Submit and iterate
for (int iteration = 1; iteration <= 5; iteration++)
{
    ConsoleUI.PrintInfo($"\n=== Submission attempt {iteration} ===");

    var response = await hubApi.SubmitLogsAsync(condensedText);
    File.AppendAllText(aiLogPath, $"--- [HUB_RESPONSE iteration={iteration}] @ {DateTime.Now:HH:mm:ss} ---\n{response}\n\n");

    if (response.Contains("{FLG:"))
    {
        ConsoleUI.PrintResult($"SUCCESS! {response}");
        return;
    }

    ConsoleUI.PrintInfo($"Feedback: {response}");

    // Find additional entries for components mentioned in feedback
    var feedbackComponents = componentRegex.Matches(response)
        .Select(m => m.Groups[1].Value)
        .Distinct()
        .ToList();

    // Get all source entries (all severities) for mentioned components
    var additionalEntries = new StringBuilder();
    if (feedbackComponents.Count > 0)
    {
        var entriesForFeedback = allDeduped
            .Where(l => feedbackComponents.Any(c => l.Contains(c, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (entriesForFeedback.Count > 0)
        {
            additionalEntries.AppendLine($"\nALL source entries for {string.Join(", ", feedbackComponents)}:");
            foreach (var line in entriesForFeedback)
                additionalEntries.AppendLine(line);
        }
    }

    var feedbackPrompt = $"""
        /no_think
        The power plant log submission was rejected. Technician feedback:

        {response}

        Current condensed logs:

        {condensedText}
        {additionalEntries}

        Create an IMPROVED version. Rules:
        - Output ONLY log lines, no commentary/headers
        - Format: [YYYY-MM-DD HH:MM] [SEVERITY] COMPONENT_ID short_description
        - CRITICAL: Output ALL lines in STRICT CHRONOLOGICAL ORDER by timestamp. Do NOT group by severity.
        - Address ALL issues from feedback — add more entries for mentioned subsystems
        - Preserve key identifiers like SAFETY_CHECK=pass verbatim
        - Stay under 4500 characters total
        - Shorten descriptions aggressively
        """;

    ConsoleUI.PrintInfo($"Asking LLM to improve (feedback prompt: {feedbackPrompt.Length} chars)...");
    var improved = await LoggedGetResponseAsync(chatClient, feedbackPrompt, $"IMPROVEMENT_{iteration}");
    condensedText = CleanLlmOutput(improved.Text);

    estimatedTokens = EstimateTokens(condensedText);
    lineCount = condensedText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
    ConsoleUI.PrintInfo($"Improved: {lineCount} lines, {condensedText.Length} chars, ~{estimatedTokens} tokens");
}

ConsoleUI.PrintError("Failed to get flag after 5 iterations.");

// --- Helper functions ---

static string CleanLlmOutput(string text)
{
    var cleaned = text.Trim();
    // Remove markdown code blocks
    cleaned = Regex.Replace(cleaned, @"^```.*$", "", RegexOptions.Multiline).Trim();
    // Remove common headers/labels
    cleaned = Regex.Replace(cleaned, @"^(OUTPUT|CONDENSED|IMPROVED|CRITICAL|ERROR|WARNING|---+|===+|Here|Below|Note|The|I ).*$",
        "", RegexOptions.Multiline | RegexOptions.IgnoreCase).Trim();
    // Remove thinking tags
    cleaned = Regex.Replace(cleaned, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase).Trim();
    // Remove empty lines
    cleaned = Regex.Replace(cleaned, @"\n\s*\n", "\n").Trim();
    // Ensure only lines starting with [ are kept (actual log entries)
    // Then sort chronologically by timestamp to guarantee correct order
    var logLines = cleaned.Split('\n')
        .Where(l => l.TrimStart().StartsWith('['))
        .OrderBy(l => l.TrimStart()) // [YYYY-MM-DD HH:MM] prefix ensures chronological sort
        .ToArray();
    return string.Join("\n", logLines);
}

static int EstimateTokens(string text)
{
    if (string.IsNullOrEmpty(text)) return 0;
    return (int)Math.Ceiling(text.Length / 3.5);
}
