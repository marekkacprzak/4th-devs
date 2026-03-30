using Microsoft.Extensions.AI;
using OkoEditor.Adapters;
using OkoEditor.Config;
using OkoEditor.Services;
using OkoEditor.Telemetry;
using OkoEditor.Tools;
using OkoEditor.UI;

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
    Console.Error.WriteLine("[WARN] Hub__ApiUrl and Hub__ApiKey will be empty. Create OkoEditor/.env with those values.");
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

// Build tools list
var tools = new List<AITool>
{
    AIFunctionFactory.Create(okoTools.CallVerifyApi),
    AIFunctionFactory.Create(okoTools.FetchOkoPage)
};

// Create LLM client
var chatClient = OpenAiClientFactory.CreateChatClient(agentConfig, telemetryConfig);

// System prompt
var systemPrompt = """
    Jesteś agentem OkoEditor. Wykonaj DOKŁADNIE te 4 wywołania API w tej kolejności, bez żadnych dodatkowych kroków.

    ══ KROK 1 - Zmień klasyfikację incydentu Skolwin na zwierzęta (MOVE04) ══
    CallVerifyApi("update", "{\"page\":\"incydenty\",\"id\":\"380792b2c86d9c5be670b3bde48e187b\",\"action\":\"update\",\"title\":\"MOVE04 Ruch zwierząt nieopodal miasta Skolwin\",\"content\":\"Zidentyfikowano bobry w okolicach rzeki przy Skolwinie. Klasyfikacja: zwierzęta (MOVE04).\"}")

    ══ KROK 2 - Oznacz zadanie Skolwin jako wykonane z wzmianką o bobrach ══
    CallVerifyApi("update", "{\"page\":\"zadania\",\"id\":\"380792b2c86d9c5be670b3bde48e187b\",\"action\":\"update\",\"done\":\"YES\",\"content\":\"Widziano bobry w okolicach Skolwina. Zadanie zakończone.\"}")

    ══ KROK 3 - Dodaj nowy incydent o ruchu ludzi w Komarowie ══
    CallVerifyApi("update", "{\"page\":\"incydenty\",\"id\":\"351c0d9c90d66b4c040fff1259dd191d\",\"action\":\"update\",\"title\":\"MOVE01 Wykryto ruch ludzi w okolicach niezamieszkanego miasta Komarowo\",\"content\":\"Czujniki zarejestrowały ruch ludzi w pobliżu opuszczonego miasta Komarowo. Rejon uznawany za niezamieszkany.\"}")

    ══ KROK 4 - Potwierdź wszystkie zmiany ══
    CallVerifyApi("done", null)

    ══ ZASADY ══
    - NIE wolno pomijać żadnego kroku
    - NIE wolno zmieniać kolejności kroków
    - NIE wolno używać FetchOkoPage - ID są już znane
    - NIE wolno używać żadnych innych akcji poza "update" i "done"
    - Po kroku 4 zakończ i wypisz otrzymaną flagę z odpowiedzi "done"
    """;

// Create orchestrator
var orchestrator = new AgentOrchestrator(chatClient, tools, systemPrompt, runLogger);

ConsoleUI.PrintBanner("OkoEditor", "OKO Surveillance System Editor Agent");
ConsoleUI.PrintInfo($"LLM: {agentConfig.Provider} / {agentConfig.Model}");
ConsoleUI.PrintInfo($"Centrala: {hubConfig.ApiUrl}");
ConsoleUI.PrintInfo($"Log file: {runLogger.FilePath}");

// Validate required config before starting — fail fast with a clear message
var configErrors = new List<string>();
if (string.IsNullOrWhiteSpace(hubConfig.ApiUrl))
    configErrors.Add("Hub__ApiUrl is empty. Set it in OkoEditor/.env: Hub__ApiUrl=https://<centrala-url>");
if (string.IsNullOrWhiteSpace(hubConfig.ApiKey))
    configErrors.Add("Hub__ApiKey is empty. Set it in OkoEditor/.env: Hub__ApiKey=<your-apikey>");
if (string.IsNullOrWhiteSpace(okoConfig.Username))
    configErrors.Add("Oko__Username is empty. Set it in OkoEditor/.env: Oko__Username=Zofia");
if (string.IsNullOrWhiteSpace(okoConfig.Password))
    configErrors.Add("Oko__Password is empty. Set it in OkoEditor/.env: Oko__Password=Zofia2026!");
if (string.IsNullOrWhiteSpace(okoConfig.AccessKey))
    configErrors.Add("Oko__AccessKey is empty. Set it in OkoEditor/.env: Oko__AccessKey=<your-apikey>");

if (configErrors.Count > 0)
{
    foreach (var err in configErrors)
        ConsoleUI.PrintError(err);
    return;
}

var userGoal = """
    Wykonaj kroki 1, 2, 3 i 4 z instrukcji dokładnie w podanej kolejności.
    Nie rób nic innego. Po kroku 4 wypisz flagę z odpowiedzi API.
    """;

await app.StartAsync();

var result = await orchestrator.RunAgentAsync(userGoal);

ConsoleUI.PrintResult(result);
runLogger.LogInfo($"Run complete. Result: {result}");

await app.StopAsync();
runLogger.Dispose();
