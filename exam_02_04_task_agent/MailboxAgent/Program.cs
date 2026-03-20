using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using MailboxAgent.Config;
using MailboxAgent.Services;
using MailboxAgent.Telemetry;
using MailboxAgent.UI;

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
var zmailApi = new ZmailApiClient(httpClient, hubConfig);
// Set up logging
var dataDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "data"));
Directory.CreateDirectory(dataDir);
var logPath = Path.Combine(dataDir, "agent.log");
File.WriteAllText(logPath, $"=== Agent Log Session {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n\n");
ConsoleUI.PrintInfo($"Agent log: {logPath}");

void Log(string message)
{
    var line = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
    File.AppendAllText(logPath, line);
}

// 3. Start investigation
ConsoleUI.PrintBanner("MAILBOX", "Email inbox investigation agent");

// State tracking
string? foundDate = null;
string? foundPassword = null;
string? foundCode = null;
var allMessages = new List<string>(); // collected message bodies

// Helper: fetch a message and add to collection
async Task<string?> FetchMessage(string id)
{
    ConsoleUI.PrintToolCall("GetMessages", $"id={id}");
    var result = await zmailApi.GetMessagesAsync(int.TryParse(id, out var rid) ? (object)rid : id);

    try
    {
        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        if (root.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
        {
            var item = items[0];
            if (item.TryGetProperty("message", out var msg))
            {
                var body = msg.GetString() ?? "";
                var from = item.TryGetProperty("from", out var f) ? f.GetString() : "?";
                var subj = item.TryGetProperty("subject", out var s) ? s.GetString() : "?";
                allMessages.Add(body);
                Log($"FETCHED [{from}] {subj}: {body}");
                ConsoleUI.PrintInfo($"Message [{from}] {subj}: {body[..Math.Min(200, body.Length)]}...");
                return body;
            }
        }
    }
    catch { }

    ConsoleUI.PrintError($"Failed to parse message: {result[..Math.Min(200, result.Length)]}");
    return null;
}

// Helper: search and return items
async Task<JsonElement[]> SearchEmails(string query)
{
    ConsoleUI.PrintToolCall("Search", $"query={query}");
    var result = await zmailApi.SearchAsync(query);

    try
    {
        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        if (root.TryGetProperty("items", out var items))
        {
            return items.EnumerateArray().Select(x => x.Clone()).ToArray();
        }
    }
    catch { }

    return [];
}

// Helper: extract values from a message using regex + LLM fallback
void ExtractFromMessage(string messageBody)
{
    // Extract date: look for YYYY-MM-DD pattern
    var dateMatch = Regex.Match(messageBody, @"\b(20\d{2}-\d{2}-\d{2})\b");
    if (dateMatch.Success)
    {
        foundDate = dateMatch.Groups[1].Value;
        Log($"EXTRACTED date: {foundDate}");
        ConsoleUI.PrintInfo($"  >> Found date: {foundDate}");
    }

    // Extract SEC code: look for SEC- followed by 28+ hex chars
    // Prefer the LONGEST valid match (corrected code has more chars)
    var secMatches = Regex.Matches(messageBody, @"SEC-([a-f0-9]{28,})");
    foreach (Match m in secMatches)
    {
        var candidate = "SEC-" + m.Groups[1].Value;
        // Always prefer longer codes (corrected version is longer)
        if (foundCode == null || candidate.Length > foundCode.Length)
        {
            foundCode = candidate;
            Log($"EXTRACTED code ({candidate.Length} chars): {foundCode}");
            ConsoleUI.PrintInfo($"  >> Found SEC code ({candidate.Length} chars): {foundCode}");
        }
    }

    // Extract password: look for explicit password patterns
    // IMPORTANT: "hasłem:\nRABARBAR25" pattern - password on next line after "hasłem:"
    // Must check multi-line patterns FIRST (most specific), then single-line
    var pwdPatterns = new (string pattern, RegexOptions opts)[]
    {
        // "logować się hasłem:\nPASSWORD" or "hasłem:\nPASSWORD"
        (@"has[łl]em[:\s]*\n\s*(\S+)", RegexOptions.IgnoreCase | RegexOptions.Multiline),
        // "hasłem: PASSWORD" (same line)
        (@"has[łl]em[:\s]+(\S+)", RegexOptions.IgnoreCase),
        // "password:\nPASSWORD" or "password: PASSWORD"
        (@"password[:\s]*\n\s*(\S+)", RegexOptions.IgnoreCase | RegexOptions.Multiline),
        (@"password[:\s]+(\S+)", RegexOptions.IgnoreCase),
        // "hasło to: X" or "hasło: X" (but NOT "hasło dostępowe" - require : or = before value)
        (@"has[łl]o\s*[:=]\s*(\S+)", RegexOptions.IgnoreCase),
    };

    foreach (var (pattern, opts) in pwdPatterns)
    {
        var pwdMatch = Regex.Match(messageBody, pattern, opts);
        if (pwdMatch.Success)
        {
            var candidate = pwdMatch.Groups[1].Value.Trim().TrimEnd('.', ',', ';', '!', '?');
            // Don't accept SEC- codes, common Polish words, or short junk as passwords
            if (!candidate.StartsWith("SEC-") &&
                candidate.Length >= 4 &&
                !IsCommonPolishWord(candidate))
            {
                foundPassword = candidate;
                Log($"EXTRACTED password: {foundPassword} (pattern: {pattern})");
                ConsoleUI.PrintInfo($"  >> Found password: {foundPassword}");
                break;
            }
        }
    }
}

bool IsCommonPolishWord(string word)
{
    var common = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "dostępowe", "dostepowe", "nowe", "stare", "systemu", "pracowniczego",
        "bezpieczeństwa", "bezpieczenstwa", "twoje", "nasze", "jest", "było",
        "zmieniliśmy", "zmienilismy", "proszę", "prosze", "logować", "logowac",
    };
    return common.Contains(word);
}

// Track fetched message IDs to avoid duplicates
var fetchedIds = new HashSet<string>();

async Task FetchAndExtract(string id)
{
    if (fetchedIds.Contains(id)) return;
    fetchedIds.Add(id);
    var body = await FetchMessage(id);
    if (body != null) ExtractFromMessage(body);
}

// === STEP 1: Search for Wiktor's emails from proton.me ===
ConsoleUI.PrintInfo("=== Step 1: Searching for emails from proton.me ===");
var wiktorEmails = await SearchEmails("from:proton.me");
ConsoleUI.PrintInfo($"Found {wiktorEmails.Length} emails from proton.me");

foreach (var email in wiktorEmails)
{
    var msgId = email.GetProperty("messageID").GetString() ?? "";
    ConsoleUI.PrintInfo($"  - subject={email.GetProperty("subject").GetString()}");
    await FetchAndExtract(msgId);
}

// Check thread replies for Wiktor's thread
foreach (var email in wiktorEmails)
{
    var threadId = email.GetProperty("threadID").GetInt32();
    ConsoleUI.PrintInfo($"=== Step 1b: Checking thread {threadId} for replies ===");
    ConsoleUI.PrintToolCall("GetThread", $"threadID={threadId}");
    var threadResult = await zmailApi.GetThreadAsync(threadId);

    try
    {
        using var doc = JsonDocument.Parse(threadResult);
        if (doc.RootElement.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                var msgId = item.GetProperty("messageID").GetString() ?? "";
                await FetchAndExtract(msgId);
            }
        }
    }
    catch { }
}

// === STEP 2: Search for SEC- ticket thread ===
ConsoleUI.PrintInfo("=== Step 2: Searching for SEC- confirmation codes ===");
var secEmails = await SearchEmails("subject:SEC-");
ConsoleUI.PrintInfo($"Found {secEmails.Length} SEC- emails");

foreach (var email in secEmails)
{
    var msgId = email.GetProperty("messageID").GetString() ?? "";
    await FetchAndExtract(msgId);
}

// === STEP 3: Search for password ===
ConsoleUI.PrintInfo("=== Step 3: Searching for password-related emails ===");
var passwordSearches = new[] { "hasło", "password", "haslo", "credentials", "login", "konto" };
foreach (var query in passwordSearches)
{
    if (foundPassword != null) break;

    var results = await SearchEmails(query);
    ConsoleUI.PrintInfo($"  Search '{query}': {results.Length} results");

    foreach (var email in results)
    {
        if (foundPassword != null) break;
        var msgId = email.GetProperty("messageID").GetString() ?? "";
        await FetchAndExtract(msgId);
    }
}

// === STEP 4: If still missing password, browse full inbox ===
if (foundPassword == null)
{
    ConsoleUI.PrintInfo("=== Step 4: Browsing full inbox for password ===");

    for (int page = 1; page <= 4; page++)
    {
        if (foundPassword != null) break;

        ConsoleUI.PrintToolCall("GetInbox", $"page={page}");
        var inboxResult = await zmailApi.GetInboxAsync(page);

        try
        {
            using var doc = JsonDocument.Parse(inboxResult);
            if (!doc.RootElement.TryGetProperty("items", out var items)) break;

            foreach (var item in items.EnumerateArray())
            {
                if (foundPassword != null) break;
                var msgId = item.GetProperty("messageID").GetString() ?? "";
                await FetchAndExtract(msgId);
            }
        }
        catch { break; }
    }
}

// === STEP 5: Submit and iterate ===
ConsoleUI.PrintInfo("=== Step 5: Submitting answer ===");
ConsoleUI.PrintInfo($"  Date: {foundDate ?? "NOT FOUND"}");
ConsoleUI.PrintInfo($"  Password: {foundPassword ?? "NOT FOUND"}");
ConsoleUI.PrintInfo($"  Code: {foundCode ?? "NOT FOUND"}");

for (int attempt = 1; attempt <= 8; attempt++)
{
    if (foundDate == null || foundPassword == null || foundCode == null)
    {
        ConsoleUI.PrintInfo($"Missing values. Waiting 15s for new emails (attempt {attempt})...");
        await Task.Delay(15_000);

        // Re-search for missing values
        foreach (var query in new[] { "hasło", "password", "from:proton.me", "subject:SEC-" })
        {
            var results = await SearchEmails(query);
            foreach (var email in results)
            {
                var msgId = email.GetProperty("messageID").GetString() ?? "";
                await FetchAndExtract(msgId);
            }
        }

        ConsoleUI.PrintInfo($"After re-search - Date: {foundDate ?? "?"}, Password: {foundPassword ?? "?"}, Code: {foundCode ?? "?"}");

        if (foundDate == null || foundPassword == null || foundCode == null)
            continue;
    }

    ConsoleUI.PrintInfo($"=== Submit attempt {attempt} ===");
    ConsoleUI.PrintInfo($"  Submitting: password={foundPassword}, date={foundDate}, code={foundCode}");
    Log($"SUBMIT attempt={attempt}: password={foundPassword}, date={foundDate}, code={foundCode}");
    var response = await hubApi.SubmitAnswerAsync(foundPassword!, foundDate!, foundCode!);

    Log($"HUB_RESPONSE attempt={attempt}: {response}");

    if (response.Contains("{FLG:"))
    {
        ConsoleUI.PrintResult($"SUCCESS! {response}");
        return;
    }

    ConsoleUI.PrintInfo($"Hub feedback: {response}");

    // On rejection, check what's probably wrong
    // If "Invalid answer payload" - structural issue, likely password is wrong format
    if (response.Contains("Invalid answer payload") || response.Contains("-970"))
    {
        ConsoleUI.PrintInfo("Payload format error - resetting password (likely wrong value)");
        foundPassword = null;
    }
    else
    {
        // Generic rejection - try re-fetching everything
        ConsoleUI.PrintInfo("Re-searching for all values...");
        fetchedIds.Clear();
        foundPassword = null;
        foundDate = null;
        foundCode = null;
    }
}

ConsoleUI.PrintError("Failed to get flag after all attempts.");
