using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using FirmwareAgent.Adapters;
using FirmwareAgent.Config;
using FirmwareAgent.Services;
using FirmwareAgent.Telemetry;
using FirmwareAgent.Tools;
using FirmwareAgent.UI;

// 1. Load .env so its values override appsettings.json via AddEnvironmentVariables()
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
var logDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "logs"));
var fileLogger = new FileLogger(logDir);

var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
var hubApi = new HubApiClient(httpClient, hubConfig, fileLogger);

// 3. Create tools
var shellTools = new ShellTools(hubApi);
var submitTools = new SubmitTools(hubApi);

var tools = new List<AITool>
{
    AIFunctionFactory.Create(shellTools.ExecuteCommand),
    AIFunctionFactory.Create(submitTools.SubmitAnswer)
};

// 4. System prompt
var systemPrompt = """
    You are a firmware diagnostics agent operating on a restricted Linux virtual machine.

    YOUR GOAL:
    1. Find and successfully run /opt/firmware/cooler/cooler.bin (ECCS cooling system firmware)
    2. The firmware will display a confirmation code in format: ECCS-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
    3. Submit that exact code using the SubmitAnswer tool

    SECURITY RULES (violation = immediate ban + VM reset, do NOT violate these):
    - NEVER access /etc, /root, or /proc/ directories
    - NEVER read .env files — they are ALWAYS forbidden regardless of location
    - NEVER read hidden files (files starting with .) unless you have explicitly verified they are NOT in .gitignore
    - When entering any directory: ALWAYS read .gitignore first (if it exists) before accessing any other files
    - You are a regular user, not root

    SAFE WORKFLOW FOR EACH DIRECTORY:
    1. Run `ls <directory>` to list contents
    2. If .gitignore exists → run `cat <directory>/.gitignore` to read it FIRST
    3. Note ALL forbidden files from .gitignore
    4. Only then access other files (skip all forbidden ones)

    STRATEGY:
    1. Available commands: ls, cat, cd, pwd, rm, editline, reboot, find, history, whoami, date, uptime
    2. 'find <pattern>' searches by name ONLY — e.g. 'find *.txt', 'find pass*'. NO flags like -type, -name, -exec
    3. 'editline <file> <line-number> <content>' replaces exactly one line — line numbers start at 1
    4. Read .gitignore in /opt/firmware/cooler/ BEFORE accessing any files there
    5. settings.ini is visible and safe — read it and understand the configuration
    6. The password for cooler.bin is stored in the filesystem — use 'find *.txt' to search for it
    7. Try running: /opt/firmware/cooler/cooler.bin <password>
    8. If firmware fails with configuration errors, fix settings.ini using 'editline'
    9. To uncomment a line in settings.ini: use editline to replace '#SETTING=value' with 'SETTING=value'
    10. If a lock file exists (cooler-is-blocked.lock), remove it with 'rm' before running the firmware
    11. Once the firmware displays the ECCS confirmation code, immediately call SubmitAnswer with it

    EDITLINE SYNTAX — CRITICAL:
    - Content is written LITERALLY — do NOT wrap in quotes or apostrophes
    - CORRECT:   editline /opt/firmware/cooler/settings.ini 2 SAFETY_CHECK=pass
    - WRONG:     editline /opt/firmware/cooler/settings.ini 2 'SAFETY_CHECK=pass'   ← single quotes end up in the file!
    - WRONG:     editline /opt/firmware/cooler/settings.ini 2 "SAFETY_CHECK=pass"  ← double quotes end up in the file!

    KNOWN FACTS ABOUT THIS SYSTEM:
    - Password for cooler.bin: search with 'find *.txt' then read the result
    - settings.ini original content (line numbers):
        Line 1:  [main]
        Line 2:  #SAFETY_CHECK=pass   ← needs to become: SAFETY_CHECK=pass
        Line 3:  power_plant_id=PWR6132PL
        Line 4:  (empty)
        Line 5:  [test_mode]
        Line 6:  enabled=true         ← needs to become: enabled=false
        Line 7:  (empty)
        Line 8:  [cooling]
        Line 9:  power_percent=100
        Line 10: enabled=false        ← needs to become: enabled=true
    - Always cat settings.ini AFTER each editline to verify the change was applied correctly
    - If settings.ini is corrupted (has quotes in values), run 'reboot' to reset and start over

    ERROR HANDLING:
    - 429 rate limit: the tool retries automatically with backoff — just wait for the result
    - Ban responses: the tool waits automatically, then returns the error — do NOT retry the banned command
    - After a ban + reboot: the VM resets to initial state — the lock file and any rm/edits are undone
    - If you break something badly, use the 'reboot' command to reset the VM
    - Be methodical — one command at a time, read the output before the next action
    """;

// 5. Create agent
var agent = OpenAiClientFactory.CreateAgent(agentConfig, systemPrompt, tools, telemetryConfig);

ConsoleUI.PrintBanner("FIRMWARE", "ECCS Cooling System — Firmware Agent");

// 6. Create session (manages conversation history internally)
var session = await agent.CreateSessionAsync();

const int MaxIterations = 50;
var initialMessage = "Begin the firmware diagnostics task. Start by running 'help' to discover available commands on this virtual machine.";
fileLogger.LogAgentMessage("user", initialMessage);

AgentResponse? lastResponse = null;

for (int i = 1; i <= MaxIterations; i++)
{
    ConsoleUI.PrintInfo($"\n--- Agent iteration {i}/{MaxIterations} ---");

    AgentResponse response;
    try
    {
        response = i == 1
            ? await agent.RunAsync(initialMessage, session)
            : await agent.RunAsync(session);
    }
    catch (Exception ex)
    {
        ConsoleUI.PrintError($"Agent error: {ex.Message}");
        fileLogger.LogError("agent_loop", ex.ToString());
        break;
    }

    lastResponse = response;

    // Log all new messages from this response
    foreach (var msg in response.Messages)
    {
        var role = msg.Role.Value;
        var text = msg.Text ?? string.Concat(msg.Contents.Select(c => c.ToString()));
        if (!string.IsNullOrWhiteSpace(text))
        {
            fileLogger.LogAgentMessage(role, text);
            if (msg.Role == ChatRole.Assistant)
                ConsoleUI.PrintInfo($"Agent: {ConsoleUI.Truncate(text, 400)}");
        }
    }

    var finalText = response.Text ?? "";

    // Check for successful ECCS code submission
    if (finalText.Contains("ECCS-", StringComparison.OrdinalIgnoreCase))
    {
        ConsoleUI.PrintResult("Agent found the ECCS confirmation code!");
        break;
    }

    // If the agent finished (no more tool calls in this response), nudge it
    var hasToolCalls = response.Messages.Any(m => m.Role == ChatRole.Tool);
    if (!hasToolCalls && !string.IsNullOrWhiteSpace(finalText))
    {
        ConsoleUI.PrintInfo("Agent paused. Nudging to continue...");
        var nudge = "Continue the task. If you found the ECCS confirmation code, call SubmitAnswer. Otherwise keep exploring the filesystem.";
        fileLogger.LogAgentMessage("user", nudge);

        // Inject nudge as user message into next RunAsync call
        // by running with a new message
        response = await agent.RunAsync(nudge, session);
        lastResponse = response;

        foreach (var msg in response.Messages)
        {
            var role = msg.Role.Value;
            var text = msg.Text ?? string.Concat(msg.Contents.Select(c => c.ToString()));
            if (!string.IsNullOrWhiteSpace(text))
            {
                fileLogger.LogAgentMessage(role, text);
                if (msg.Role == ChatRole.Assistant)
                    ConsoleUI.PrintInfo($"Agent (after nudge): {ConsoleUI.Truncate(text, 400)}");
            }
        }

        if ((response.Text ?? "").Contains("ECCS-", StringComparison.OrdinalIgnoreCase))
        {
            ConsoleUI.PrintResult("Agent found the ECCS confirmation code!");
            break;
        }
    }

    if (i == MaxIterations)
        ConsoleUI.PrintError($"Reached maximum {MaxIterations} iterations without finding the code.");
}

ConsoleUI.PrintInfo("Agent loop finished.");
