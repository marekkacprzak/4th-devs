using System.Text.Json;
using Microsoft.Extensions.Configuration;
using EvaluationAgent.Adapters;
using EvaluationAgent.Config;
using EvaluationAgent.Models;
using EvaluationAgent.Services;
using EvaluationAgent.Telemetry;
using EvaluationAgent.UI;

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
var sensorDownloader = new SensorDataDownloader(httpClient, hubConfig.SensorsZipUrl);
var chatClient = OpenAiClientFactory.CreateChatClient(agentConfig, telemetryConfig);
var noteValidator = new OperatorNoteValidator(chatClient);

// 3. Banner
ConsoleUI.PrintBanner("EVALUATION", "Power plant sensor anomaly detection");
ConsoleUI.PrintInfo($"Hub API: {hubConfig.ApiUrl}");
ConsoleUI.PrintInfo($"LLM: {agentConfig.Provider} / {agentConfig.Model} @ {agentConfig.Endpoint}");

// 4. Download and extract sensor data
var dataDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "data"));
Directory.CreateDirectory(dataDir);

var logPath = Path.Combine(dataDir, $"run_{DateTime.Now:yyyyMMdd_HHmmss}.log");
FileLogger.Initialize(logPath);
ConsoleUI.PrintInfo($"Log file: {logPath}");

var sensorsDir = await sensorDownloader.DownloadAndExtractAsync(dataDir);

// 5. Load all JSON sensor files
ConsoleUI.PrintInfo("Loading sensor files...");

var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var readings = new List<SensorReading>();
int parseErrors = 0;

foreach (var filePath in Directory.GetFiles(sensorsDir, "*.json").OrderBy(f => f))
{
    var fileId = Path.GetFileNameWithoutExtension(filePath);
    try
    {
        var json = await File.ReadAllTextAsync(filePath);
        var data = JsonSerializer.Deserialize<SensorData>(json, jsonOptions);
        if (data == null)
        {
            parseErrors++;
            continue;
        }
        readings.Add(new SensorReading { FileId = fileId, Data = data });
    }
    catch
    {
        parseErrors++;
    }
}

ConsoleUI.PrintInfo($"Loaded {readings.Count} sensor readings ({parseErrors} parse errors)");

// 6. Programmatic anomaly analysis
ConsoleUI.PrintInfo("Running programmatic analysis...");
SensorAnalyzer.AnalyzeAll(readings);

int programmaticAnomalies = readings.Count(r => r.Anomalies != AnomalyType.None);
ConsoleUI.PrintInfo($"After programmatic analysis: {programmaticAnomalies} files with anomalies");

// 7. Operator note validation (LLM)
ConsoleUI.PrintInfo("Validating operator notes...");
await noteValidator.ValidateAllAsync(readings);

// 8. Collect all anomalous file IDs
var anomalousIds = readings
    .Where(r => r.Anomalies != AnomalyType.None)
    .Select(r => r.FileId)
    .OrderBy(id => id)
    .ToList();

ConsoleUI.PrintInfo($"Total anomalous files: {anomalousIds.Count}");

// Print breakdown
var outOfRange = readings.Count(r => r.Anomalies.HasFlag(AnomalyType.OutOfRange));
var inactiveSensor = readings.Count(r => r.Anomalies.HasFlag(AnomalyType.InactiveSensorNonZero));
var falseOk = readings.Count(r => r.Anomalies.HasFlag(AnomalyType.OperatorFalseOk));
var falseError = readings.Count(r => r.Anomalies.HasFlag(AnomalyType.OperatorFalseError));

ConsoleUI.PrintInfo($"  OutOfRange: {outOfRange}");
ConsoleUI.PrintInfo($"  InactiveSensorNonZero: {inactiveSensor}");
ConsoleUI.PrintInfo($"  OperatorFalseOk: {falseOk}");
ConsoleUI.PrintInfo($"  OperatorFalseError: {falseError}");

if (anomalousIds.Count == 0)
{
    ConsoleUI.PrintError("No anomalies found — check sensor data and analysis logic.");
    return;
}

// 9. Submit to Hub
ConsoleUI.PrintInfo($"\nSubmitting {anomalousIds.Count} IDs to Hub...");
var response = await hubApi.SubmitEvaluationAsync(anomalousIds);

if (response.Contains("{FLG:"))
{
    ConsoleUI.PrintResult($"SUCCESS! Flag received: {response}");
}
else
{
    ConsoleUI.PrintInfo($"Hub response: {response}");
    ConsoleUI.PrintInfo("No flag in response — check anomaly detection logic.");
}
