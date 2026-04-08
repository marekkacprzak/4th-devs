using Microsoft.Extensions.AI;
using ShellAccess.Adapters;
using ShellAccess.Config;
using ShellAccess.Services;
using ShellAccess.Telemetry;
using ShellAccess.Tools;
using ShellAccess.UI;

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
    Console.Error.WriteLine("[WARN] Hub__VerifyUrl and Hub__ApiKey will be empty. Create ShellAccess/.env with those values.");
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

// Create shared services
var runLogger = new RunLogger();
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
var centralaApi = new CentralaApiClient(httpClient, hubConfig, runLogger);

ConsoleUI.PrintBanner("ShellAccess", "Remote Shell Intelligence Agent");
ConsoleUI.PrintInfo($"Agent LLM: {agentConfig.Provider} / {agentConfig.Model}");
ConsoleUI.PrintInfo($"Hub VerifyUrl: {hubConfig.VerifyUrl}");
ConsoleUI.PrintInfo($"Log file: {runLogger.FilePath}");

// Validate required config before starting
var configErrors = new List<string>();
if (string.IsNullOrWhiteSpace(hubConfig.VerifyUrl))
    configErrors.Add("Hub__VerifyUrl is empty. Set it in ShellAccess/.env: Hub__VerifyUrl=https://<hub_url>/verify");
if (string.IsNullOrWhiteSpace(hubConfig.ApiKey))
    configErrors.Add("Hub__ApiKey is empty. Set it in ShellAccess/.env: Hub__ApiKey=<your-apikey>");

if (configErrors.Count > 0)
{
    foreach (var err in configErrors)
        ConsoleUI.PrintError(err);
    return;
}

await app.StartAsync();

// Create the shell tool and register it
var shellTools = new ShellTools(centralaApi);
var tools = new List<AITool>
{
    AIFunctionFactory.Create(shellTools.ExecuteShellCommand)
};

// Create the chat client
var agentClient = OpenAiClientFactory.CreateChatClient(agentConfig, telemetryConfig);

var systemPrompt = """
    You are an expert Linux investigator. You have access to a remote server via ExecuteShellCommand.

    DATA FILES IN /data:
    - time_logs.csv  — CSV with columns: date;description;location_id;place_id  (4541 lines, semicolon-delimited)
    - locations.json — JSON array of objects: {"location_id": N, "name": "CityName"}
    - gps.json       — JSON array of objects: {"latitude": X, "longitude": Y, "type": "...", "location_id": N, "entry_id": M}

    TASK: Find when Rafał's body/corpse was discovered. The log entry describes finding a body.

    EXECUTE THESE STEPS IN ORDER — do NOT skip ahead, do NOT repeat completed steps:

    STEP 1 — Find the discovery entry (body was found = "znalezion" in Polish):
      grep -i "znalezion" /data/time_logs.csv | head -10

    STEP 2 — From the matching line, extract:
      - DATE (field 1, e.g. 2024-11-13)
      - LOCATION_ID (field 3, e.g. 219)
      - ENTRY_ID (field 4, e.g. 954634)

    STEP 3 — Get city name by looking up LOCATION_ID in locations.json:
      grep -A2 "location_id.*LOCATION_ID" /data/locations.json | head -5

    STEP 4 — Get GPS coordinates by looking up ENTRY_ID in gps.json:
      grep -B10 '"entry_id": ENTRY_ID' /data/gps.json | head -15

    STEP 5 — Compute date ONE DAY BEFORE the finding date:
      date -d 'FINDING_DATE - 1 day' +%Y-%m-%d

    STEP 6 — Submit the answer using echo (this submits to the verification system):
      echo '{"date":"COMPUTED_DATE","city":"CITY_NAME","longitude":LON,"latitude":LAT}'

    CRITICAL RULES:
    - ALWAYS pipe grep output to | head -N to stay under 4096 byte server limit
    - longitude and latitude MUST be numbers (not strings) in the JSON
    - The "date" MUST be ONE DAY BEFORE the finding date (the day before the body was found)
    - Do NOT repeat a step you already completed — work forward through the steps
    - After the echo command returns a response with a flag ({{...}}), report it as the final result
    """;

var userGoal = """
    Follow the STEPS in your instructions to find when Rafał's body was found and submit the JSON answer.
    Start with STEP 1: grep -i "znalezion" /data/time_logs.csv | head -10
    """;

var orchestrator = new AgentOrchestrator(agentClient, tools, systemPrompt, runLogger);

ConsoleUI.PrintPhase("Starting agent — exploring remote server /data");
runLogger.LogInfo("Agent starting. User goal: " + userGoal);

var result = await orchestrator.RunAgentAsync(userGoal);

ConsoleUI.PrintResult(result);
runLogger.LogInfo($"Run complete. Result: {result}");

await app.StopAsync();
runLogger.Dispose();
