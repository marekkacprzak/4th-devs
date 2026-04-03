using FoodWareHouse.Adapters;
using FoodWareHouse.Config;
using FoodWareHouse.Services;
using FoodWareHouse.Telemetry;
using FoodWareHouse.Tools;
using FoodWareHouse.UI;

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
else
{
    Console.Error.WriteLine($"[WARN] .env file not found at: {envPath}");
    Console.Error.WriteLine("[WARN] Hub__ApiUrl and Hub__ApiKey will be empty. Create FoodWareHouse/.env with those values.");
}

var builder = WebApplication.CreateBuilder(args);

// Load configuration
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
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
var centralaApi = new CentralaApiClient(httpClient, hubConfig, runLogger);
var foodTools = new FoodWareHouseTools(centralaApi, httpClient);

// LLM client (available as fallback for parsing ambiguous responses)
var chatClient = OpenAiClientFactory.CreateChatClient(agentConfig, telemetryConfig);

// Create orchestrator
var orchestrator = new FoodWareHouseOrchestrator(foodTools, chatClient, runLogger, hubConfig.Food4CitiesUrl);

ConsoleUI.PrintBanner("FoodWareHouse", "Food Warehouse Distribution Agent — AI Devs 4");
ConsoleUI.PrintInfo($"LLM: {agentConfig.Provider} / {agentConfig.Model}");
ConsoleUI.PrintInfo($"Centrala: {hubConfig.ApiUrl}");
ConsoleUI.PrintInfo($"Task: {hubConfig.TaskName}");
ConsoleUI.PrintInfo($"Food4Cities URL: {hubConfig.Food4CitiesUrl}");
ConsoleUI.PrintInfo($"Log file: {runLogger.FilePath}");

// Validate required config before starting — fail fast with a clear message
var configErrors = new List<string>();
if (string.IsNullOrWhiteSpace(hubConfig.ApiUrl))
    configErrors.Add("Hub__ApiUrl is empty. Set it in FoodWareHouse/.env: Hub__ApiUrl=https://<hub_url>");
if (string.IsNullOrWhiteSpace(hubConfig.ApiKey))
    configErrors.Add("Hub__ApiKey is empty. Set it in FoodWareHouse/.env: Hub__ApiKey=<your-apikey>");

if (configErrors.Count > 0)
{
    foreach (var err in configErrors)
        ConsoleUI.PrintError(err);
    runLogger.Dispose();
    return;
}

await app.StartAsync();

var result = await orchestrator.RunAsync();

ConsoleUI.PrintResult(result);
runLogger.LogInfo($"Run complete. Result: {result}");

await app.StopAsync();
runLogger.Dispose();
