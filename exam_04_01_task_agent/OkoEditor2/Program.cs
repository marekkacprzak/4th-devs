using Microsoft.Extensions.AI;
using OkoEditor2.Adapters;
using OkoEditor2.Config;
using OkoEditor2.Services;
using OkoEditor2.Telemetry;
using OkoEditor2.Tools;
using OkoEditor2.UI;

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
}

var builder = WebApplication.CreateBuilder(args);

// Load configuration
var agentConfig = new AgentConfig();
builder.Configuration.GetSection("Agent").Bind(agentConfig);

var hubConfig = new HubConfig();
builder.Configuration.GetSection("Hub").Bind(hubConfig);

var okoConfig = new OkoConfig();
builder.Configuration.GetSection("Oko").Bind(okoConfig);

var telemetryConfig = new TelemetryConfig();
builder.Configuration.GetSection("Telemetry").Bind(telemetryConfig);

var app = builder.Build();

using var telemetry = new TelemetrySetup(telemetryConfig);

// Create services
var runLogger = new RunLogger();
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var centralaApi = new CentralaApiClient(httpClient, hubConfig, runLogger);
var okoTools = new OkoTools(centralaApi, okoConfig, httpClient);

// Build two tool lists
// Phase 1 (Discovery): both tools available
var allTools = new List<AITool>
{
    AIFunctionFactory.Create(okoTools.CallVerifyApi),
    AIFunctionFactory.Create(okoTools.FetchOkoPage)
};

// Phase 3 (Execution): only CallVerifyApi — prevents looping back to reading
var apiOnlyTools = new List<AITool>
{
    AIFunctionFactory.Create(okoTools.CallVerifyApi)
};

// Create LLM client
var chatClient = OpenAiClientFactory.CreateChatClient(agentConfig, telemetryConfig);

// Create phased orchestrator
var orchestrator = new PhasedOrchestrator(chatClient, allTools, apiOnlyTools, runLogger, okoTools, okoConfig);

ConsoleUI.PrintBanner("OkoEditor2", "Autonomous OKO Surveillance System Editor");
ConsoleUI.PrintInfo($"LLM: {agentConfig.Provider} / {agentConfig.Model}");
ConsoleUI.PrintInfo($"Centrala: {hubConfig.ApiUrl}");
ConsoleUI.PrintInfo($"Log file: {runLogger.FilePath}");
ConsoleUI.PrintInfo("Mode: AUTONOMOUS — agent discovers IDs and coding system from OKO pages");

// Validate required config before starting
var configErrors = new List<string>();
if (string.IsNullOrWhiteSpace(hubConfig.ApiUrl))
    configErrors.Add("Hub__ApiUrl is empty. Set it in OkoEditor2/.env: Hub__ApiUrl=https://<centrala-url>");
if (string.IsNullOrWhiteSpace(hubConfig.ApiKey))
    configErrors.Add("Hub__ApiKey is empty. Set it in OkoEditor2/.env: Hub__ApiKey=<your-apikey>");
if (string.IsNullOrWhiteSpace(okoConfig.Username))
    configErrors.Add("Oko__Username is empty. Set it in OkoEditor2/.env: Oko__Username=Zofia");
if (string.IsNullOrWhiteSpace(okoConfig.Password))
    configErrors.Add("Oko__Password is empty. Set it in OkoEditor2/.env: Oko__Password=Zofia2026!");
if (string.IsNullOrWhiteSpace(okoConfig.AccessKey))
    configErrors.Add("Oko__AccessKey is empty. Set it in OkoEditor2/.env: Oko__AccessKey=<your-apikey>");

if (configErrors.Count > 0)
{
    foreach (var err in configErrors)
        ConsoleUI.PrintError(err);
    return;
}

await app.StartAsync();

var result = await orchestrator.RunAsync();

ConsoleUI.PrintResult(result);
runLogger.LogInfo($"Run complete. Result: {result}");

await app.StopAsync();
runLogger.Dispose();
