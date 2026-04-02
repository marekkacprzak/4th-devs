using Filesystem.Adapters;
using Filesystem.Config;
using Filesystem.Services;
using Filesystem.Telemetry;
using Filesystem.Tools;
using Filesystem.UI;

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

var telemetryConfig = new TelemetryConfig();
builder.Configuration.GetSection("Telemetry").Bind(telemetryConfig);

var app = builder.Build();

using var telemetry = new TelemetrySetup(telemetryConfig);

// Create services
var runLogger = new RunLogger();
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
var centralaApi = new CentralaApiClient(httpClient, hubConfig, runLogger);
var filesystemTools = new FilesystemTools(centralaApi, httpClient);

// LLM client for note parsing
var chatClient = OpenAiClientFactory.CreateChatClient(agentConfig, telemetryConfig);

// Create orchestrator
var orchestrator = new FilesystemOrchestrator(filesystemTools, chatClient, runLogger, hubConfig.NotesUrl);

ConsoleUI.PrintBanner("Filesystem", "Trade Notes Agent — AI Devs 4");
ConsoleUI.PrintInfo($"LLM: {agentConfig.Provider} / {agentConfig.Model}");
ConsoleUI.PrintInfo($"Centrala: {hubConfig.ApiUrl}");
ConsoleUI.PrintInfo($"Task: {hubConfig.TaskName}");
ConsoleUI.PrintInfo($"Log file: {runLogger.FilePath}");

// Validate required config
var configErrors = new List<string>();
if (string.IsNullOrWhiteSpace(hubConfig.ApiUrl))
    configErrors.Add("Hub__ApiUrl is empty. Set it in .env: Hub__ApiUrl=https://<hub-url>");
if (string.IsNullOrWhiteSpace(hubConfig.ApiKey))
    configErrors.Add("Hub__ApiKey is empty. Set it in .env: Hub__ApiKey=<your-apikey>");

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
