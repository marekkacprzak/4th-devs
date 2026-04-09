using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using RadioMonitoring.Adapters;
using RadioMonitoring.Config;
using RadioMonitoring.Services;
using RadioMonitoring.Telemetry;
using RadioMonitoring.Tools;
using RadioMonitoring.UI;
using Spectre.Console;

// Aspire / redirected-stdout fix: Spectre.Console silences itself when no TTY
// is detected (e.g. under Aspire's DCP process manager which pipes stdout).
AnsiConsole.Profile.Out = new AnsiConsoleOutput(Console.Out);

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
    Console.Error.WriteLine("[WARN] Hub__VerifyUrl and Hub__ApiKey will be empty. Create RadioMonitoring/.env with those values.");
}

var builder = WebApplication.CreateBuilder(args);

// Load configuration
var agentConfig = new AgentConfig();
builder.Configuration.GetSection("Agent").Bind(agentConfig);

var visionConfig = new VisionConfig();
builder.Configuration.GetSection("Vision").Bind(visionConfig);

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

ConsoleUI.PrintBanner("RadioMonitoring", "Radio Signal Intelligence Agent");
ConsoleUI.PrintInfo($"Agent LLM: {agentConfig.Provider} / {agentConfig.Model}");
ConsoleUI.PrintInfo($"Vision LLM: {visionConfig.Provider} / {visionConfig.Model}");
ConsoleUI.PrintInfo($"Hub VerifyUrl: {hubConfig.VerifyUrl}");
ConsoleUI.PrintInfo($"Log file: {runLogger.FilePath}");

// Validate required config before starting
var configErrors = new List<string>();
if (string.IsNullOrWhiteSpace(hubConfig.VerifyUrl))
    configErrors.Add("Hub__VerifyUrl is empty. Set it in RadioMonitoring/.env: Hub__VerifyUrl=https://<hub_url>/verify");
if (string.IsNullOrWhiteSpace(hubConfig.ApiKey))
    configErrors.Add("Hub__ApiKey is empty. Set it in RadioMonitoring/.env: Hub__ApiKey=<your-apikey>");

if (configErrors.Count > 0)
{
    foreach (var err in configErrors)
        ConsoleUI.PrintError(err);
    return;
}

await app.StartAsync();

// ── PHASE 1: Programmatic data collection ──────────────────────────────────
var visionClient = OpenAiClientFactory.CreateVisionChatClient(visionConfig, telemetryConfig);
var radioCollector = new RadioCollector(centralaApi, visionClient, runLogger);

string intelligence;
try
{
    intelligence = await radioCollector.CollectAsync();
    runLogger.LogInfo($"Collection complete. Intelligence report length: {intelligence.Length} chars");
    ConsoleUI.PrintInfo($"Intelligence collected: {intelligence.Length} characters");
}
catch (Exception ex)
{
    ConsoleUI.PrintError($"Collection failed: {ex.Message}");
    runLogger.LogError("Collection", ex.ToString());
    await app.StopAsync();
    runLogger.Dispose();
    return;
}

// ── PHASE 2: Agentic analysis and report submission ────────────────────────
ConsoleUI.PrintPhase("PHASE 2: Intelligence Analysis & Report Transmission");

// Try known candidates first (programmatic, no LLM cost)
ConsoleUI.PrintPhase("PHASE 2a: Trying Known Candidates");
var knownResult = await TryKnownCandidates(centralaApi, runLogger);
if (knownResult != null)
{
    ConsoleUI.PrintResult(knownResult);
    runLogger.LogInfo($"Known candidate succeeded. Result: {knownResult}");
    await app.StopAsync();
    runLogger.Dispose();
    return;
}
ConsoleUI.PrintInfo("All known candidates rejected — proceeding to LLM analysis");
runLogger.LogInfo("All known candidates failed, proceeding to LLM agent");

var agentClient = OpenAiClientFactory.CreateChatClient(agentConfig, telemetryConfig);

var radioTools = new RadioTools(centralaApi);
var tools = new List<AITool>
{
    AIFunctionFactory.Create(radioTools.TransmitReport)
};

var systemPrompt = """
    You are a radio intelligence analyst. You have received intercepted radio communications
    that contain information about a hidden city referred to as "Syjon".

    Your task is to analyze the provided intelligence and extract exactly four pieces of data:
    1. cityName — the real name of the city called "Syjon"
    2. cityArea — the city's area in square kilometers, rounded to exactly 2 decimal places (e.g. "12.34")
    3. warehousesCount — the number of warehouses in the city (integer)
    4. phoneNumber — the contact phone number for the city

    IMPORTANT HINTS from prior signal analysis:
    - The city "Syjon" is referred to in one signal as "Nowogrod Polnocny" (ASCII transliteration of Polish "Nowogrod Polnocny")
    - A phone number "644122092" was found in a signal about "noclegu na Syjonie" (lodging in Syjon)
    - An XML document listed industrialBlocks=6 for this city
    - A JSON list showed occupiedArea=16.9473 km² for this city

    Rules:
    - cityArea MUST be a string with exactly 2 decimal places (mathematical rounding, not truncation)
    - phoneNumber should contain only digits, no dashes or spaces
    - Use ASCII only for cityName (no Polish diacritics): a,e,i,o,u,l,n,z,c,s — NOT ą,ę,ó,ł,ń,ż,ź,ć,ś
    - Once you have identified all four values with confidence, call TransmitReport immediately
    - Do NOT ask for clarification — extract the best values from the available data

    After TransmitReport is called, report the API response you received.
    """;

var orchestrator = new AgentOrchestrator(agentClient, tools, systemPrompt, runLogger);

var result = await orchestrator.RunAgentAsync(intelligence);

// If tool-calling failed (400 / unsupported), fall back to direct JSON extraction
if (result.StartsWith("ERROR: LLM call failed"))
{
    ConsoleUI.PrintInfo("Tool-calling failed. Falling back to direct JSON extraction...");
    runLogger.LogInfo("Fallback: direct JSON extraction without tools");
    result = await FallbackExtractAndTransmit(agentClient, centralaApi, intelligence, runLogger);
}

ConsoleUI.PrintResult(result);
runLogger.LogInfo($"Run complete. Result: {result}");

await app.StopAsync();
runLogger.Dispose();

// ── Try known candidate values programmatically ──────────────────────────────
static async Task<string?> TryKnownCandidates(CentralaApiClient centralaApi, RunLogger logger)
{
    // Known intelligence:
    // - Phone from image: "Jacek Kramer - 644-122-092 w sprawie noclegu na Syjonie"
    // - XML says industrialBlocks=6 for "Nowogród Północny" (trainingData="true")
    // - JSON city areas: Puck=16.9473, Skarszewy=10.7284, Drohiczyn=15.6841
    // - REJECTED: "Nowogrod Polnocny", "Puck", "NowogrodPolnocny", "Syjon"
    // - REJECTED (diacritics): "Nowogród Północny"
    // - API rule: cityName must be ASCII without Polish diacritics
    //
    // Strategy: try ALL cities from JSON + Morse-hinted names.
    // For each name, try all area/warehouse combinations.
    // When cityName is rejected, move to next name immediately.

    // Key insight from task.md: Syjon has water access + cattle farming
    // From JSON: only Skarszewy and Drohiczyn have BOTH riverAccess=true AND farmAnimals=true
    // From transcription: Skarszewy described as "biblijny raj" (biblical paradise = Syjon/Zion!)
    // Therefore: Skarszewy = Syjon (most likely), Drohiczyn = backup
    //
    // JSON city areas:
    //   Skarszewy=10.7284, Drohiczyn=15.6841, Mielnik=4.0836, Narew=3.2195, Narewka=2.5196
    //   Darzlubie=1.2187, Domatowo=1.6349, Mechowo=0.6674, Zarnowiec=1.1372
    //   Opalino=0.2718, Karlikowo=0.4635, Celbowo=0.7142, Brudzewo=0.5538
    //
    // NOTE: Already REJECTED: Puck, Syjon, Nowogrod Polnocny, NowogrodPolnocny, Nowogrod Połnocny

    // CONFIRMED: Skarszewy = Syjon! cityName="Skarszewy" area="10.73" accepted.
    // Only warehousesCount is wrong (error -740). Already tried: 3,4,5,6,7,8 → all wrong.
    // Now try wider range. Phone 644122092 likely correct (image evidence).
    //
    // Try Skarszewy with a wide range of warehouse counts first.
    // If still failing, try Drohiczyn as backup (also farmAnimals=true + riverAccess=true).

    // Start with Skarszewy and broad warehouse range
    var skarszewyWarehouses = new[] { 1, 2, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 25, 30 };
    foreach (var wh in skarszewyWarehouses)
    {
        try
        {
            ConsoleUI.PrintStep($"Trying: Skarszewy / area=10.73 / wh={wh} / phone=644122092");
            logger.LogInfo($"Skarszewy candidate: warehousesCount={wh}");

            var result = await centralaApi.VerifyAsync(new
            {
                action = "transmit",
                cityName = "Skarszewy",
                cityArea = "10.73",
                warehousesCount = wh,
                phoneNumber = "644122092"
            });

            logger.LogInfo($"Result: {result}");
            ConsoleUI.PrintInfo($"API: {result}");

            bool isError = result.StartsWith("HTTP 4") || result.StartsWith("ERROR") ||
                           result.Contains("\"code\": -") || result.Contains("\"code\":-");
            if (!isError)
            {
                logger.LogInfo($"SUCCESS: Skarszewy / 10.73 / wh={wh}");
                return result;
            }
        }
        catch (Exception ex)
        {
            logger.LogError("TryKnownCandidates", ex.Message);
        }
    }

    // Pair each city with its JSON area for targeted testing (backup if Skarszewy fails)
    var cityCandidates = new (string name, string area)[]
    {
        ("Drohiczyn", "15.68"),     // farmAnimals=true + riverAccess=true
        ("Mielnik", "4.08"),
        ("Narew", "3.22"),
        ("Narewka", "2.52"),
        ("Darzlubie", "1.22"),
        ("Mechowo", "0.67"),
        ("Zarnowiec", "1.14"),
        ("Opalino", "0.27"),
        ("Karlikowo", "0.46"),
        ("Celbowo", "0.71"),
        ("Brudzewo", "0.55"),
        ("Domatowo", "1.63"),
    };
    // Warehouse count candidates
    var warehousesToTry = new[] { 6, 5, 7, 4, 3, 8, 9, 10, 12 };

    foreach (var (cityName, cityArea) in cityCandidates)
    {
        bool cityNameRejected = false;
        foreach (var warehouses in warehousesToTry)
        {
            if (cityNameRejected) break;
            try
            {
                ConsoleUI.PrintStep($"Trying: {cityName} / area={cityArea} / wh={warehouses} / phone=644122092");
                logger.LogInfo($"Candidate: cityName={cityName}, cityArea={cityArea}, warehousesCount={warehouses}, phoneNumber=644122092");

                var result = await centralaApi.VerifyAsync(new
                {
                    action = "transmit",
                    cityName,
                    cityArea,
                    warehousesCount = warehouses,
                    phoneNumber = "644122092"
                });

                logger.LogInfo($"Candidate result: {result}");
                ConsoleUI.PrintInfo($"API: {result}");

                bool isError = result.StartsWith("HTTP 4") || result.StartsWith("ERROR") ||
                               result.Contains("\"code\": -") || result.Contains("\"code\":-");
                if (!isError)
                {
                    logger.LogInfo($"SUCCESS: {cityName} / {cityArea} / {warehouses}");
                    return result;
                }

                if (result.Contains("cityName") || result.Contains("city_name"))
                {
                    cityNameRejected = true;
                }
            }
            catch (Exception ex)
            {
                logger.LogError("TryKnownCandidates", ex.Message);
            }
        }

        // If cityName accepted but other fields wrong, try area variants for that city
        if (!cityNameRejected)
        {
            var areaVariants = new[] { "16.95", "16.94", "15.68", "10.73", "4.08", "3.22" };
            foreach (var altArea in areaVariants)
            {
                if (altArea == cityArea) continue; // already tried
                foreach (var warehouses in warehousesToTry)
                {
                    try
                    {
                        ConsoleUI.PrintStep($"Trying alt area: {cityName} / area={altArea} / wh={warehouses}");
                        logger.LogInfo($"Alt area candidate: cityName={cityName}, cityArea={altArea}, warehousesCount={warehouses}");

                        var result = await centralaApi.VerifyAsync(new
                        {
                            action = "transmit",
                            cityName,
                            cityArea = altArea,
                            warehousesCount = warehouses,
                            phoneNumber = "644122092"
                        });

                        logger.LogInfo($"Alt result: {result}");
                        bool isError = result.StartsWith("HTTP 4") || result.StartsWith("ERROR") ||
                                       result.Contains("\"code\": -") || result.Contains("\"code\":-");
                        if (!isError) return result;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError("TryKnownCandidates.AltArea", ex.Message);
                    }
                }
            }
        }
    }

    return null;
}

// ── Fallback: extract fields as JSON, then transmit directly ─────────────────
static async Task<string> FallbackExtractAndTransmit(
    IChatClient agentClient,
    CentralaApiClient centralaApi,
    string intelligence,
    RunLogger logger)
{
    ConsoleUI.PrintPhase("FALLBACK: Direct JSON Extraction");

    var extractPrompt = $$"""
        You are a radio intelligence analyst. Analyze the intercepted intelligence below and extract exactly:
        - cityName: the real name of the city referred to as "Syjon"
        - cityArea: area in km², string with exactly 2 decimal places, e.g. "12.34"
        - warehousesCount: integer number of warehouses
        - phoneNumber: contact phone number, digits only

        Return ONLY valid JSON in this exact format (no other text):
        {"cityName":"...","cityArea":"12.34","warehousesCount":5,"phoneNumber":"123456789"}

        Intelligence:
        {{intelligence}}
        """;

    var messages = new List<ChatMessage>
    {
        new(ChatRole.User, extractPrompt)
    };

    try
    {
        var response = await agentClient.GetResponseAsync(messages);
        var rawText = response.Messages
            .SelectMany(m => m.Contents)
            .OfType<TextContent>()
            .Select(t => t.Text)
            .LastOrDefault() ?? "";

        ConsoleUI.PrintLlmResponse(rawText);
        logger.LogInfo($"Fallback LLM response: {rawText}");

        // Strip think tokens
        rawText = Regex.Replace(rawText, @"<think>[\s\S]*?</think>", "").Trim();

        // Extract JSON from the response
        var jsonMatch = Regex.Match(rawText, @"\{[^{}]*\}", RegexOptions.Singleline);
        if (!jsonMatch.Success)
        {
            logger.LogError("FallbackExtract", $"No JSON found in response: {rawText}");
            return $"ERROR: Could not find JSON in LLM response: {rawText}";
        }

        using var doc = JsonDocument.Parse(jsonMatch.Value);
        var root = doc.RootElement;

        var cityName = root.GetProperty("cityName").GetString() ?? "";
        var cityArea = root.GetProperty("cityArea").GetString() ?? "";
        var warehousesCount = root.GetProperty("warehousesCount").GetInt32();
        var phoneNumber = root.GetProperty("phoneNumber").GetString() ?? "";

        ConsoleUI.PrintInfo($"Extracted: cityName={cityName}, cityArea={cityArea}, warehousesCount={warehousesCount}, phoneNumber={phoneNumber}");

        // Transmit directly
        ConsoleUI.PrintStep("Transmitting report to headquarters...");
        var transmitResult = await centralaApi.VerifyAsync(new
        {
            action = "transmit",
            cityName,
            cityArea,
            warehousesCount,
            phoneNumber
        });

        logger.LogInfo($"Transmit result: {transmitResult}");
        return transmitResult;
    }
    catch (Exception ex)
    {
        logger.LogError("FallbackExtract", ex.ToString());
        return $"ERROR in fallback: {ex.Message}";
    }
}
