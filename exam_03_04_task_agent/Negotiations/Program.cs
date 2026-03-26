using Negotiations.Adapters;
using Negotiations.Config;
using Negotiations.Models;
using Negotiations.Services;
using Negotiations.Telemetry;
using Negotiations.Tools;
using Negotiations.UI;

// Load .env file so its values override appsettings.json via environment variables
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

var builder = WebApplication.CreateBuilder(args);

// Load configuration
var agentConfig = new AgentConfig();
builder.Configuration.GetSection("Agent").Bind(agentConfig);

var hubConfig = new HubConfig();
builder.Configuration.GetSection("Hub").Bind(hubConfig);

var reactorConfig = new ReactorConfig();
builder.Configuration.GetSection("Reactor").Bind(reactorConfig);

var telemetryConfig = new TelemetryConfig();
builder.Configuration.GetSection("Telemetry").Bind(telemetryConfig);

// Configure JSON serialization
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var app = builder.Build();

using var telemetry = new TelemetrySetup(telemetryConfig);

// Create services
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var logger = new InteractionLogger("logs");
var csvData = new CsvDataService(httpClient, hubConfig);

await csvData.LoadAsync();

var chatClient = OpenAiClientFactory.CreateChatClient(agentConfig, telemetryConfig);
var matcher = new ItemMatcherService(chatClient, csvData, logger);
var searchTool = new SearchTool(csvData, matcher, logger);

ConsoleUI.PrintBanner("NEGOTIATIONS", "negotiations tool agent");
ConsoleUI.PrintInfo($"Log file: {logger.LogFilePath}");
ConsoleUI.PrintInfo($"Server listening on port {reactorConfig.Port}");
ConsoleUI.PrintInfo($"LLM: {agentConfig.Provider} / {agentConfig.Model}");
ConsoleUI.PrintInfo($"Expose via ngrok: ngrok http {reactorConfig.Port}");

// Inactivity check: after 30s of no tool calls, poll hub for result
var inactivityCts = new CancellationTokenSource();
async Task ResetInactivityTimer()
{
    inactivityCts.Cancel();
    inactivityCts = new CancellationTokenSource();
    var token = inactivityCts.Token;
    ConsoleUI.PrintStep("Waiting 30s for more requests before checking hub result...");
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(30), token);

        var checkPayload = $$$"""
            {"apikey":"{{{hubConfig.ApiKey}}}","task":"{{{hubConfig.TaskName}}}","answer":{"action":"check"}}
            """;

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            ConsoleUI.PrintStep($"Checking hub result (attempt {attempt}/5)...");
            var response = await httpClient.PostAsync(
                hubConfig.ApiUrl,
                new StringContent(checkPayload, System.Text.Encoding.UTF8, "application/json"),
                token);
            var body = await response.Content.ReadAsStringAsync(token);
            await logger.LogInfo($"Hub check result (attempt {attempt}): {body}");

            if (!body.Contains("-500"))
            {
                ConsoleUI.PrintResult(body);
                break;
            }

            ConsoleUI.PrintStep("Not ready yet, retrying in 30s...");
            await Task.Delay(TimeSpan.FromSeconds(30), token);
        }
    }
    catch (TaskCanceledException) { /* reset — new request arrived */ }
}

// Tool endpoint
app.MapPost("/api/search", async (ToolRequest request) =>
{
    ConsoleUI.PrintIncomingRequest("/api/search", request.Params);
    await logger.LogApiInteraction("IN", "/api/search", request.Params);
    _ = ResetInactivityTimer();

    try
    {
        var result = await searchTool.SearchItemsAsync(request.Params);

        await logger.LogApiInteraction("OUT", "/api/search", result);
        return Results.Json(new ToolResponse(result));
    }
    catch (Exception ex)
    {
        ConsoleUI.PrintError($"Error: {ex.Message}");
        await logger.LogApiInteraction("ERROR", "/api/search", ex.Message);
        return Results.Json(new ToolResponse("Błąd wewnętrzny. Spróbuj ponownie."));
    }
});

// Tool endpoint #2 — list available items
app.MapPost("/api/items", async (ToolRequest request) =>
{
    ConsoleUI.PrintIncomingRequest("/api/items", request.Params);
    await logger.LogApiInteraction("IN", "/api/items", request.Params);
    _ = ResetInactivityTimer();

    var names = csvData.GetAllItemNames();
    var result = string.Join(", ", names);
    if (System.Text.Encoding.UTF8.GetByteCount(result) > 490)
        result = string.Join(", ", names.Take(20)) + ", ...";

    await logger.LogApiInteraction("OUT", "/api/items", result);
    return Results.Json(new ToolResponse(result));
});

// Configure port
app.Urls.Clear();
app.Urls.Add($"http://0.0.0.0:{reactorConfig.Port}");

app.Run();
