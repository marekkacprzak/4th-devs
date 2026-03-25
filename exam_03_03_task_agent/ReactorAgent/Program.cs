using System.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ReactorAgent.Adapters;
using ReactorAgent.Config;
using ReactorAgent.Services;
using ReactorAgent.Telemetry;
using ReactorAgent.Tools;
using ReactorAgent.UI;

// ── 1. Load configuration ──────────────────────────────────────────────────
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

var hubConfig = new HubConfig();
configuration.GetSection("Hub").Bind(hubConfig);

var agentConfig = new AgentConfig();
configuration.GetSection("Agent").Bind(agentConfig);

var telemetryConfig = new TelemetryConfig();
configuration.GetSection("Telemetry").Bind(telemetryConfig);

if (string.IsNullOrWhiteSpace(hubConfig.ApiKey))
{
    ConsoleUI.PrintError("Hub__ApiKey is not set. Fill in ReactorAgent/.env before running.");
    return;
}

// ── 2. Initialise telemetry & logging ─────────────────────────────────────
using var telemetry = new TelemetrySetup(telemetryConfig);

var logsDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
using var requestLogger = new RequestLogger(logsDir);
ConsoleUI.PrintInfo($"HTTP log: {requestLogger.LogFilePath}");

// ── 3. Create services ────────────────────────────────────────────────────
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var hubApi = new HubApiClient(httpClient, hubConfig, requestLogger);
var navigator = new ReactorNavigator();
var reactorTools = new ReactorTools(hubApi, navigator);

// ── 4. Create AI Agent with LLM logging ───────────────────────────────────
var (chatClient, agentLogger) = OpenAiClientFactory.CreateChatClientWithLogger(
    agentConfig, telemetryConfig, logsDir);
if (agentLogger != null)
    ConsoleUI.PrintInfo($"Agent log: {agentLogger.LogFilePath}");

var tools = reactorTools.GetAIFunctions().Cast<AITool>().ToList();

const string SystemInstructions = """
    You are a robot navigation agent for a nuclear reactor maintenance task.
    Your goal: guide the robot from column 1 to column 7 on row 5 of a 7x5 grid, avoiding moving reactor blocks.

    Strategy:
    1. Start the session by calling SendCommand with "start".
    2. Repeatedly: call DecideNextMove() to get the safest command, then call SendCommand() with that command.
    3. Stop when the response contains "SUCCESS" or "FLG:" or the robot reaches column 7.
    4. Maximum 50 steps total.
    """;

var agent = chatClient.AsAIAgent(
    instructions: SystemInstructions,
    name: "ReactorAgent",
    description: null,
    tools: tools,
    loggerFactory: null,
    services: null);

// ── 5. Main agent loop ────────────────────────────────────────────────────
ConsoleUI.PrintBanner("REACTOR", "Navigate the robot safely");

var activitySource = new ActivitySource(telemetryConfig.ServiceName);
using var mainSpan = activitySource.StartActivity("reactor.navigate");

var session = await agent.CreateSessionAsync();
bool success = false;

const string TaskPrompt = "Navigate the robot from column 1 to column 7. Call SendCommand(\"start\") first, then loop: DecideNextMove() → SendCommand(result). Stop when the goal is reached.";

ConsoleUI.PrintStep("Starting agent session");

try
{
    var response = await agent.RunAsync(TaskPrompt, session, options: null);
    var responseText = response.Text ?? "";

    ConsoleUI.PrintInfo($"Agent response: {responseText[..Math.Min(200, responseText.Length)]}");

    if (reactorTools.LastRawResponse.Contains("{FLG:") ||
        responseText.Contains("SUCCESS") ||
        responseText.Contains("FLG:"))
    {
        mainSpan?.SetTag("result", "success");
        success = true;
        ConsoleUI.PrintResult($"Goal reached! {reactorTools.LastRawResponse}");
    }
    else if (reactorTools.CurrentBoard?.IsGoalReached == true)
    {
        mainSpan?.SetTag("result", "goal_reached");
        success = true;
        ConsoleUI.PrintResult("Robot reached column 7 — goal achieved!");
    }
}
catch (Exception ex)
{
    mainSpan?.SetStatus(ActivityStatusCode.Error, ex.Message);
    ConsoleUI.PrintError($"Agent error: {ex.Message}");
}

if (!success)
{
    mainSpan?.SetTag("result", "agent_failed");
    ConsoleUI.PrintStep("Fallback: pure deterministic navigation");
    await RunDeterministicFallback(hubApi, navigator, reactorTools, activitySource, requestLogger);
}

agentLogger?.Dispose();

// ── 6. Deterministic fallback ─────────────────────────────────────────────
static async Task RunDeterministicFallback(
    HubApiClient hubApi,
    ReactorNavigator navigator,
    ReactorTools reactorTools,
    ActivitySource activitySource,
    RequestLogger logger)
{
    using var fallbackSpan = activitySource.StartActivity("reactor.fallback");
    logger.LogComment("=== FALLBACK: deterministic navigation ===");

    if (reactorTools.CurrentBoard == null)
    {
        ConsoleUI.PrintInfo("[Fallback] Sending 'start'...");
        await reactorTools.SendCommand("start");
    }

    const int MaxSteps = 50;
    for (int step = 1; step <= MaxSteps; step++)
    {
        using var stepSpan = activitySource.StartActivity("reactor.fallback.step");
        stepSpan?.SetTag("step", step);

        var board = reactorTools.CurrentBoard;
        if (board == null) break;

        if (board.IsGoalReached)
        {
            ConsoleUI.PrintResult("[Fallback] Robot reached the goal!");
            fallbackSpan?.SetTag("result", "success");
            return;
        }

        var command = navigator.DecideNextMove(board);
        ConsoleUI.PrintInfo($"[Fallback] Step {step}: {command}");
        if (command == "done") break;

        var response = await reactorTools.SendCommand(command);

        if (response.Contains("SUCCESS") || response.Contains("FLG:") ||
            reactorTools.LastRawResponse.Contains("{FLG:"))
        {
            ConsoleUI.PrintResult($"[Fallback] SUCCESS! {reactorTools.LastRawResponse}");
            fallbackSpan?.SetTag("result", "success");
            return;
        }
    }

    fallbackSpan?.SetTag("result", "failed");
    ConsoleUI.PrintError("[Fallback] Did not reach the goal.");
}
