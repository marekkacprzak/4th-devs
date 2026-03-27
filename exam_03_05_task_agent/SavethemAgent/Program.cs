using System.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using SavethemAgent.Adapters;
using SavethemAgent.Config;
using SavethemAgent.Services;
using SavethemAgent.Telemetry;
using SavethemAgent.Tools;
using SavethemAgent.UI;

// ── 1. Load .env ──────────────────────────────────────────────────────────────
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

// ── 2. Configuration ──────────────────────────────────────────────────────────
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

// Override Hub values from .env (Hub__ApiKey, Hub__ToolSearchUrl, Hub__VerifyUrl)
if (string.IsNullOrEmpty(hubConfig.ApiKey))
    hubConfig.ApiKey = Environment.GetEnvironmentVariable("Hub__ApiKey") ?? "";
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Hub__ToolSearchUrl")))
    hubConfig.ToolSearchUrl = Environment.GetEnvironmentVariable("Hub__ToolSearchUrl")!;
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Hub__VerifyUrl")))
    hubConfig.VerifyUrl = Environment.GetEnvironmentVariable("Hub__VerifyUrl")!;

if (string.IsNullOrWhiteSpace(hubConfig.ApiKey))
{
    ConsoleUI.PrintError("Hub__ApiKey is not set. Add it to SavethemAgent/.env before running.");
    return;
}

// ── 3. Initialise telemetry & logging ─────────────────────────────────────────
using var telemetry = new TelemetrySetup(telemetryConfig);

var logsDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
using var requestLogger = new RequestLogger(logsDir);
ConsoleUI.PrintInfo($"HTTP log: {requestLogger.LogFilePath}");

// ── 4. Create services ────────────────────────────────────────────────────────
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var hubApi = new HubApiClient(httpClient, hubConfig, requestLogger);
var routePlanner = new RoutePlanner();
var savethemTools = new SavethemTools(hubApi, routePlanner);

// ── 5. Create AI Agent with LLM logging ──────────────────────────────────────
var (chatClient, agentLogger) = OpenAiClientFactory.CreateChatClientWithLogger(
    agentConfig, telemetryConfig, logsDir);
if (agentLogger != null)
    ConsoleUI.PrintInfo($"Agent log: {agentLogger.LogFilePath}");

var tools = savethemTools.GetAIFunctions().Cast<AITool>().ToList();

const string SystemInstructions = """
    You are a route planning agent. Your mission is to help a messenger reach city Skolwin safely.
    You have exactly 10 food rations and 10 fuel units.

    Follow these steps IN ORDER:
    1. Call SearchAvailableTools with query "map terrain grid obstacles" to find the map tool URL.
    2. Call SearchAvailableTools with query "vehicle transport fuel consumption" to find the vehicle tool URL.
    3. Call CallTool with the map tool URL and query "Skolwin" to get the terrain map.
       - The maps tool expects a CITY NAME as the query, not a generic phrase.
    4. Call ParseAndStoreMap with the full map response JSON.
    5. Call CallTool with the vehicle tool URL and query "rocket" to get rocket vehicle data.
    6. Call CallTool with the vehicle tool URL and query "horse" to get horse vehicle data.
    7. Call CallTool with the vehicle tool URL and query "car" to get car vehicle data.
    8. Call CallTool with the vehicle tool URL and query "walk" to get walking data.
       - Note: if a vehicle query returns HTTP 404 with "Allowed values", use those exact names.
    9. Call ParseAndStoreVehicles once with each vehicle response (call it 4 times, one per vehicle).
    10. Call PlanOptimalRoute to calculate the optimal route using BFS + resource optimization.
    11. Call SubmitSolution to submit the answer.

    Important:
    - All tool queries MUST be in English.
    - Maps tool: query = destination city name (e.g. "Skolwin").
    - Vehicles tool: query = one vehicle name at a time (rocket / horse / car / walk).
    - ParseAndStoreVehicles is ADDITIVE — call it once per vehicle to accumulate all vehicle data.
    - Food: 1 unit consumed per step regardless of vehicle.
    - Fuel: depends on vehicle speed (faster = more fuel per step). Walking = 0 fuel.
    """;

var agent = chatClient.AsAIAgent(
    instructions: SystemInstructions,
    name: "SavethemAgent",
    description: null,
    tools: tools,
    loggerFactory: null,
    services: null);

// ── 6. Main agent loop ────────────────────────────────────────────────────────
ConsoleUI.PrintBanner("SAVETHEM", "Plan the route to Skolwin");

var activitySource = new ActivitySource(telemetryConfig.ServiceName);
using var mainSpan = activitySource.StartActivity("savethem.plan_route");

var session = await agent.CreateSessionAsync();
bool success = false;

const string TaskPrompt = """
    Plan the optimal route to reach city Skolwin (the goal on the map).
    Resources: 10 food rations, 10 fuel units.
    Start by searching for available tools, then get the map and vehicles, plan the route, and submit.
    """;

ConsoleUI.PrintStep("Starting agent session");

try
{
    var response = await agent.RunAsync(TaskPrompt, session, options: null);
    var responseText = response.Text ?? "";

    ConsoleUI.PrintInfo($"Agent response: {responseText[..Math.Min(300, responseText.Length)]}");

    if (savethemTools.LastVerifyResponse.Contains("FLG:") ||
        savethemTools.LastVerifyResponse.Contains("flag") ||
        responseText.Contains("FLG:") ||
        responseText.Contains("flag"))
    {
        mainSpan?.SetTag("result", "success");
        success = true;
        ConsoleUI.PrintResult($"SUCCESS! {savethemTools.LastVerifyResponse}");
    }
    else if (savethemTools.CurrentRoute?.IsValid == true &&
             !string.IsNullOrEmpty(savethemTools.LastVerifyResponse))
    {
        mainSpan?.SetTag("result", "submitted");
        success = true;
        ConsoleUI.PrintResult($"Route submitted. Response: {savethemTools.LastVerifyResponse}");
    }
}
catch (Exception ex)
{
    mainSpan?.SetStatus(ActivityStatusCode.Error, ex.Message);
    ConsoleUI.PrintError($"Agent error: {ex.Message}");
}

// ── 7. Deterministic fallback ─────────────────────────────────────────────────
if (!success)
{
    mainSpan?.SetTag("result", "agent_failed_trying_fallback");
    ConsoleUI.PrintStep("Fallback: deterministic planning");
    await RunDeterministicFallback(savethemTools, requestLogger, activitySource);
}

agentLogger?.Dispose();

// ── Fallback function ─────────────────────────────────────────────────────────
static async Task RunDeterministicFallback(
    SavethemTools tools,
    RequestLogger logger,
    ActivitySource activitySource)
{
    using var fallbackSpan = activitySource.StartActivity("savethem.fallback");
    logger.LogComment("=== FALLBACK: deterministic planning ===");

    var route = tools.CurrentRoute;
    if (route == null || !route.IsValid)
    {
        ConsoleUI.PrintError("[Fallback] No valid route available. Cannot submit.");
        fallbackSpan?.SetTag("result", "no_route");
        return;
    }

    if (string.IsNullOrEmpty(tools.LastVerifyResponse))
    {
        ConsoleUI.PrintStep("[Fallback] Submitting existing route...");
        var verifyResponse = await tools.SubmitSolution();
        ConsoleUI.PrintResult($"[Fallback] Submit result: {verifyResponse}");
        fallbackSpan?.SetTag("result", "submitted");
    }
    else
    {
        ConsoleUI.PrintInfo($"[Fallback] Already submitted. Last response: {tools.LastVerifyResponse}");
        fallbackSpan?.SetTag("result", "already_submitted");
    }
}
