using Microsoft.Extensions.AI;
using GoingThere.Adapters;
using GoingThere.Config;
using GoingThere.Services;
using GoingThere.Telemetry;
using GoingThere.Tools;
using GoingThere.UI;

// Load .env file — check current directory and parent directory
var envLoaded = false;
foreach (var envPath in new[]
{
    Path.Combine(Directory.GetCurrentDirectory(), ".env"),
    Path.Combine(Directory.GetCurrentDirectory(), "..", ".env")
})
{
    if (!File.Exists(envPath)) continue;
    foreach (var line in File.ReadAllLines(envPath))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
        var sep = trimmed.IndexOf('=');
        if (sep > 0)
            Environment.SetEnvironmentVariable(trimmed[..sep], trimmed[(sep + 1)..]);
    }
    envLoaded = true;
    break;
}

if (!envLoaded)
{
    Console.Error.WriteLine("[WARN] .env file not found. Hub__ApiUrl and Hub__ApiKey may be empty.");
}

var builder = WebApplication.CreateBuilder(args);

// Bind configuration
var agentConfig = new AgentConfig();
builder.Configuration.GetSection("Agent").Bind(agentConfig);

var hubConfig = new HubConfig();
builder.Configuration.GetSection("Hub").Bind(hubConfig);

var telemetryConfig = new TelemetryConfig();
builder.Configuration.GetSection("Telemetry").Bind(telemetryConfig);

var app = builder.Build();

using var telemetry = new TelemetrySetup(telemetryConfig);

// Create services
var runLogger = new RunLogger();
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var hubApi = new HubApiClient(httpClient, hubConfig, runLogger);
var gameTools = new GameTools(hubApi, runLogger);

// Register tools — navigation is fully programmatic, LLM just sequences two calls
var tools = new List<AITool>
{
    AIFunctionFactory.Create(gameTools.StartGame),
    AIFunctionFactory.Create(gameTools.NavigateAllColumns),
};

// Create LLM client
var chatClient = OpenAiClientFactory.CreateChatClient(agentConfig, telemetryConfig);

// System prompt — minimal, navigation handled programmatically
var systemPrompt = """
    You are a rocket navigation agent.

    Your job is simple:
    1. Call StartGame() to initialize the game.
    2. Call NavigateAllColumns() to navigate the rocket to the base. This handles everything automatically.
    3. Report the flag from the result.

    If NavigateAllColumns returns an error, call StartGame() again and then NavigateAllColumns() again.
    """;

ConsoleUI.PrintBanner("GoingThere", "Rocket Navigation Agent — OKO Grid Challenge");
ConsoleUI.PrintInfo($"LLM: {agentConfig.Provider} / {agentConfig.Model}");
ConsoleUI.PrintInfo($"Hub: {hubConfig.ApiUrl}");
ConsoleUI.PrintInfo($"Log file: {runLogger.FilePath}");

// Validate required config
var configErrors = new List<string>();
if (string.IsNullOrWhiteSpace(hubConfig.ApiUrl))
    configErrors.Add("Hub__ApiUrl is empty. Set it in .env: Hub__ApiUrl=https://<hub_url>");
if (string.IsNullOrWhiteSpace(hubConfig.ApiKey))
    configErrors.Add("Hub__ApiKey is empty. Set it in .env: Hub__ApiKey=<your-apikey>");

if (configErrors.Count > 0)
{
    foreach (var err in configErrors)
        ConsoleUI.PrintError(err);
    return;
}

var userGoal = """
    Start the game and navigate the rocket to column 12.
    Follow the PROCEDURE in your instructions exactly for every column.
    Report the flag when you reach the base.
    """;

await app.StartAsync();

var orchestrator = new AgentOrchestrator(chatClient, tools, systemPrompt, runLogger);
var result = await orchestrator.RunAgentAsync(userGoal);

ConsoleUI.PrintResult(result);
runLogger.LogInfo($"Run complete. Result: {result}");

await app.StopAsync();
runLogger.Dispose();
