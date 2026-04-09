using Microsoft.Extensions.AI;
using Phonecall2.Adapters;
using Phonecall2.Config;
using Phonecall2.Services;
using Phonecall2.Telemetry;
using Phonecall2.UI;

// ── Load .env ────────────────────────────────────────────────────────────────
var envPath = FindEnvFile(Directory.GetCurrentDirectory());
if (envPath != null)
{
    foreach (var line in File.ReadAllLines(envPath))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
        var sep = trimmed.IndexOf('=');
        if (sep > 0)
            Environment.SetEnvironmentVariable(trimmed[..sep], trimmed[(sep + 1)..]);
    }
}
else
{
    Console.Error.WriteLine("[WARN] .env file not found. Hub__ApiKey will be empty.");
}

// ── Bootstrap ────────────────────────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);

var agentConfig = new AgentConfig();
builder.Configuration.GetSection("Agent").Bind(agentConfig);

var hubConfig = new HubConfig();
builder.Configuration.GetSection("Hub").Bind(hubConfig);

var audioConfig = new AudioConfig();
builder.Configuration.GetSection("Audio").Bind(audioConfig);

var telemetryConfig = new TelemetryConfig();
builder.Configuration.GetSection("Telemetry").Bind(telemetryConfig);

var app = builder.Build();

using var telemetry = new TelemetrySetup(telemetryConfig);

// ── Services ──────────────────────────────────────────────────────────────────
var runLogger = new RunLogger();
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
var centralaClient = new CentralaApiClient(httpClient, hubConfig, runLogger);
var audioService = new LocalAudioService(httpClient, audioConfig, runLogger);
var chatClient = OpenAiClientFactory.CreateChatClient(agentConfig, telemetryConfig);
var orchestrator = new ConversationOrchestrator(centralaClient, audioService, chatClient, runLogger);

// ── Banner ────────────────────────────────────────────────────────────────────
ConsoleUI.PrintBanner("Phonecall2", "Phonecall2 Agent — macOS Zosia TTS + WhisperKit STT");
ConsoleUI.PrintInfo($"LLM: {agentConfig.Provider} / {agentConfig.Model}");
ConsoleUI.PrintInfo($"TTS: macOS say -v Zosia (Polish)");
ConsoleUI.PrintInfo($"STT: WhisperKit at {audioConfig.WhisperKitEndpoint}");
ConsoleUI.PrintInfo($"Centrala: {hubConfig.ApiUrl}");
ConsoleUI.PrintInfo($"Log file: {runLogger.FilePath}");

// ── Config validation ─────────────────────────────────────────────────────────
var errors = new List<string>();
if (string.IsNullOrWhiteSpace(hubConfig.ApiUrl))
    errors.Add("Hub__ApiUrl is empty. Set it in .env: Hub__ApiUrl=https://<hub_url>");
if (string.IsNullOrWhiteSpace(hubConfig.ApiKey))
    errors.Add("Hub__ApiKey is empty. Set it in .env: Hub__ApiKey=<your-apikey>");

if (errors.Count > 0)
{
    foreach (var err in errors)
        ConsoleUI.PrintError(err);
    return;
}

// ── Run ───────────────────────────────────────────────────────────────────────
await app.StartAsync();

var result = await orchestrator.RunConversationAsync();

ConsoleUI.PrintResult(result);
runLogger.LogInfo($"Run complete. Result: {result}");

await app.StopAsync();
runLogger.Dispose();

// ── Helpers ───────────────────────────────────────────────────────────────────
static string? FindEnvFile(string startDir)
{
    var dir = new DirectoryInfo(startDir);
    while (dir != null)
    {
        var candidate = Path.Combine(dir.FullName, ".env");
        if (File.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }
    return null;
}
