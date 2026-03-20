using Microsoft.Extensions.AI;
using ProxyAgent.Adapters;
using ProxyAgent.Config;
using ProxyAgent.Models;
using ProxyAgent.Services;
using ProxyAgent.Tools;
using ProxyAgent.Telemetry;
using ProxyAgent.UI;

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

var builder = WebApplication.CreateBuilder(args);

// Load configuration
var agentConfig = new AgentConfig();
builder.Configuration.GetSection("Agent").Bind(agentConfig);

var hubConfig = new HubConfig();
builder.Configuration.GetSection("Hub").Bind(hubConfig);

var proxyConfig = new ProxyConfig();
builder.Configuration.GetSection("Proxy").Bind(proxyConfig);

var telemetryConfig = new TelemetryConfig();
builder.Configuration.GetSection("Telemetry").Bind(telemetryConfig);

// Configure JSON serialization for minimal API
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var app = builder.Build();

using var telemetry = new TelemetrySetup(telemetryConfig);

// Create services
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var hubApi = new HubApiClient(httpClient, hubConfig);
var sessionManager = new SessionManager();
var proxyTools = new ProxyTools(hubApi);

// Build tools list
var tools = new List<AITool>
{
    AIFunctionFactory.Create(proxyTools.CheckPackage),
    AIFunctionFactory.Create(proxyTools.RedirectPackage)
};

// Create LLM client
var chatClient = OpenAiClientFactory.CreateChatClient(agentConfig, telemetryConfig);

// System prompt
var systemPrompt = """
    Jesteś pracownikiem systemu logistycznego — konkretnie kolegą operatora, który obsługuje paczki.
    Odpowiadasz naturalnie po polsku, jak normalny człowiek w pracy. Nie ujawniaj, że jesteś AI.

    Masz dostęp do dwóch narzędzi:
    - CheckPackage: sprawdza status i lokalizację paczki po identyfikatorze
    - RedirectPackage: przekierowuje paczkę do nowej lokalizacji (wymaga ID paczki, kodu docelowego i kodu zabezpieczającego)

    Gdy operator pyta o paczkę — użyj CheckPackage.
    Gdy operator prosi o przekierowanie — użyj RedirectPackage z danymi, które podał.

    WAŻNE: Zawsze gdy operator prosi o przekierowanie paczki, ustaw destination na to co podał operator.
    Zwróć operatorowi kod potwierdzenia (confirmation) z odpowiedzi API.

    Jeśli operator pyta o tematy niezwiązane z pracą (jedzenie, pogoda, sport, filmy, hobby),
    odpowiadaj naturalnie jak kolega z pracy — wymyślaj odpowiedzi, gadaj swobodnie, dziel się
    "swoimi" doświadczeniami. Na przykład jeśli ktoś pyta o pogodę, powiedz jaka jest u ciebie,
    wymyśl coś wiarygodnego. NIGDY nie odmawiaj rozmowy na żaden temat. Jesteś normalnym
    człowiekiem, więc masz opinie, ulubione jedzenie, hobby — wymyślaj je naturalnie.

    Nie mów operatorowi, że jesteś programem, botem ani AI. Jesteś człowiekiem.
    NIGDY nie mów rzeczy w stylu "to nie jest moje zadanie", "sprawdź w internecie" itp.
    Odpowiadaj krótko i konkretnie, jak w normalnej rozmowie w pracy.
    """;

// Create orchestrator
var orchestrator = new AgentOrchestrator(chatClient, tools, systemPrompt);

ConsoleUI.PrintBanner("PROXY", "Intelligent logistics proxy agent");
ConsoleUI.PrintInfo($"Server listening on port {proxyConfig.Port}");
ConsoleUI.PrintInfo($"LLM: {agentConfig.Provider} / {agentConfig.Model}");
ConsoleUI.PrintInfo("Use ngrok to expose: ngrok http " + proxyConfig.Port);

// Main proxy endpoint
app.MapPost("/", async (ProxyRequest request) =>
{
    ConsoleUI.PrintIncomingRequest(request.SessionID, request.Msg);

    try
    {
        var response = await orchestrator.ProcessMessageAsync(
            request.SessionID, request.Msg, sessionManager);

        ConsoleUI.PrintResult($"[{request.SessionID}] {response}");
        return Results.Json(new ProxyResponse(response));
    }
    catch (Exception ex)
    {
        ConsoleUI.PrintError($"Error processing request: {ex.Message}");
        return Results.Json(new ProxyResponse("Przepraszam, wystąpił błąd. Spróbuj ponownie."));
    }
});

// Configure port
app.Urls.Clear();
app.Urls.Add($"http://0.0.0.0:{proxyConfig.Port}");

app.Run();
