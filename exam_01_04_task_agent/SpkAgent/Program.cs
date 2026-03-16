using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using SpkAgent.Adapters;
using SpkAgent.Config;
using SpkAgent.Services;
using SpkAgent.Telemetry;
using SpkAgent.Tools;
using SpkAgent.UI;

// 1. Load configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var agentConfig = new AgentConfig();
configuration.GetSection("Agent").Bind(agentConfig);

var visionConfig = new VisionConfig();
configuration.GetSection("Vision").Bind(visionConfig);

var hubConfig = new HubConfig();
configuration.GetSection("Hub").Bind(hubConfig);

var telemetryConfig = new TelemetryConfig();
configuration.GetSection("Telemetry").Bind(telemetryConfig);

using var telemetry = new TelemetrySetup(telemetryConfig);

// 2. Create services
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var hubApi = new HubApiClient(httpClient, hubConfig);
var chatClient = OpenAiClientFactory.CreateChatClient(agentConfig, telemetryConfig);

var basePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
var docsDir = Path.Combine(basePath, "docs");

var docTools = new DocTools(httpClient, hubConfig.DocsBaseUrl, docsDir);
var imageTools = new ImageTools(httpClient, visionConfig, docsDir);
var declarationTools = new DeclarationTools();

// 3. Run pipeline
ConsoleUI.PrintBanner("SPK AGENT", "Hybrid pipeline: C# orchestration + Vision LLM");

// --- STEP 1: Download all documentation ---
ConsoleUI.PrintInfo("=== STEP 1: Downloading documentation ===");
var downloadResult = await docTools.DownloadAllDocs();
ConsoleUI.PrintInfo(downloadResult);

// --- STEP 2: Read key documentation ---
ConsoleUI.PrintInfo("=== STEP 2: Reading documentation ===");
var indexContent = await File.ReadAllTextAsync(Path.Combine(docsDir, "index.md"));
var templateContent = await File.ReadAllTextAsync(Path.Combine(docsDir, "zalacznik-E.md"));
var routeMapContent = await File.ReadAllTextAsync(Path.Combine(docsDir, "zalacznik-F.md"));
var wagonContent = await File.ReadAllTextAsync(Path.Combine(docsDir, "dodatkowe-wagony.md"));
var glossaryContent = await File.ReadAllTextAsync(Path.Combine(docsDir, "zalacznik-G.md"));

ConsoleUI.PrintInfo($"Read index.md ({indexContent.Length} chars)");
ConsoleUI.PrintInfo($"Read zalacznik-E.md ({templateContent.Length} chars) - declaration template");
ConsoleUI.PrintInfo($"Read zalacznik-F.md ({routeMapContent.Length} chars) - route map");
ConsoleUI.PrintInfo($"Read dodatkowe-wagony.md ({wagonContent.Length} chars) - wagon rules");
ConsoleUI.PrintInfo($"Read zalacznik-G.md ({glossaryContent.Length} chars) - glossary");

// --- STEP 3: Process image to CSV using Vision LLM ---
ConsoleUI.PrintInfo("=== STEP 3: Processing image (trasy-wylaczone.png -> CSV) via Vision LLM ===");
var csvResult = await imageTools.ProcessImageToCSV("trasy-wylaczone.png", "trasy-wylaczone.csv");
ConsoleUI.PrintInfo(csvResult);

var csvPath = Path.Combine(docsDir, "trasy-wylaczone.csv");
var csvContent = File.Exists(csvPath) ? await File.ReadAllTextAsync(csvPath) : "";
ConsoleUI.PrintInfo($"Disabled routes CSV:\n{csvContent}");

// --- STEP 4: Analyze with LLM ---
ConsoleUI.PrintInfo("=== STEP 4: Analyzing documentation with LLM ===");

var analysisPrompt =
    "You are analyzing SPK (System Przesyłek Konduktorskich) documentation to fill a transport declaration.\n\n" +
    "WAGON RULES:\n" + wagonContent + "\n\n" +
    "GLOSSARY: WDP = Wagony Dodatkowe Płatne (Additional Paid Wagons)\n\n" +
    "DISABLED ROUTES CSV:\n" + csvContent + "\n\n" +
    "ROUTE MAP:\n" + routeMapContent + "\n\n" +
    "FEE RULES:\n" +
    "- Category A (Strategic): base fee 0 PP, exempt from all fees\n" +
    "- For A and B: wagon fee not charged\n" +
    "- Section 8.3: disabled routes can ONLY be used for categories A and B\n\n" +
    "SHIPMENT: Gdańsk→Żarnowiec, 2800 kg, reactor fuel cassettes, category A, sender 450202122\n\n" +
    "PREVIOUS ATTEMPTS:\n" +
    "- WDP=4: error 'The shipment will not fit on the train.'\n" +
    "- WDP=0: error 'The shipment will not fit on the train.'\n\n" +
    "Train: base 2 wagons × 500 kg = 1000 kg. Each extra wagon: 500 kg.\n" +
    "For 2800 kg: need ceil((2800-1000)/500) = 4 extra wagons → 3000 kg capacity.\n\n" +
    "QUESTION: What WDP value should we use? What else could cause 'won't fit'?\n" +
    "Consider: maybe WDP counts total wagons (not just additional), or maybe weight needs adjustment.\n" +
    "Reply concisely with your recommended WDP value and reasoning.";

var analysisResponse = await chatClient.GetResponseAsync(analysisPrompt);
ConsoleUI.PrintInfo($"LLM Analysis: {analysisResponse.Text}");

// --- STEP 5 & 6: Build and submit with auto-retry ---
ConsoleUI.PrintInfo("=== STEP 5 & 6: Building and submitting declaration with auto-retry ===");

var today = DateTime.Now.ToString("yyyy-MM-dd");

// Try different WDP values: 4 (calculated), 0 (free category), 5, 6, 3
int[] wdpValues = [4, 0, 5, 6, 3, 2, 1];
var success = false;

foreach (var wdp in wdpValues)
{
    ConsoleUI.PrintInfo($"\n--- Trying WDP={wdp} ---");

    var declaration = declarationTools.BuildDeclaration(
        date: today,
        origin: "Gdańsk",
        destination: "Żarnowiec",
        senderId: "450202122",
        routeCode: "X-01",
        category: "A",
        contents: "kasety z paliwem do reaktora",
        weightKg: 2800,
        wdp: wdp,
        specialNotes: "BRAK",
        paymentAmount: "0 PP");

    ConsoleUI.PrintInfo(declaration);

    var response = await hubApi.SubmitDeclarationAsync(declaration);

    if (response.Contains("{FLG:"))
    {
        ConsoleUI.PrintResult($"SUCCESS with WDP={wdp}! {response}");
        success = true;
        break;
    }

    ConsoleUI.PrintError($"WDP={wdp} failed: {response}");

    // If not a "won't fit" error, analyze with LLM
    if (!response.Contains("-760"))
    {
        ConsoleUI.PrintInfo("Different error detected, asking LLM for analysis...");
        var errorAnalysis = await chatClient.GetResponseAsync(
            $"SPK declaration error: {response}\n\nDeclaration:\n{declaration}\n\n" +
            $"Documentation:\n{indexContent[..2000]}\n\nTemplate:\n{templateContent}\n\n" +
            "What is wrong? How to fix? Reply with specific field values to change.");
        ConsoleUI.PrintInfo($"LLM Error Analysis: {errorAnalysis.Text}");
        break; // Don't keep trying WDP values if error is different
    }
}

if (!success)
{
    ConsoleUI.PrintError("All WDP values failed. Possible issues:");
    ConsoleUI.PrintInfo("1. Route X-01 may need to be opened first (use exam_01_05_task_agent)");
    ConsoleUI.PrintInfo("2. Declaration format may need adjustment");
    ConsoleUI.PrintInfo("3. Weight or other field may need different values");
}
