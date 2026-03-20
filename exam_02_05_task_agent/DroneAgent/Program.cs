using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using DroneAgent.Adapters;
using DroneAgent.Config;
using DroneAgent.Services;
using DroneAgent.Telemetry;
using DroneAgent.Tools;
using DroneAgent.UI;

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

var visionConfig = new VisionConfig();
configuration.GetSection("Vision").Bind(visionConfig);

var hubConfig = new HubConfig();
configuration.GetSection("Hub").Bind(hubConfig);

var telemetryConfig = new TelemetryConfig();
configuration.GetSection("Telemetry").Bind(telemetryConfig);

using var telemetry = new TelemetrySetup(telemetryConfig);

// 2. Create services
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
var hubApi = new HubApiClient(httpClient, hubConfig);

var dataDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "data"));
Directory.CreateDirectory(dataDir);
var aiLogPath = Path.Combine(dataDir, "ai_requests.log");
File.WriteAllText(aiLogPath, $"=== AI Log Session {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n\n");

var mapUrl = $"{hubConfig.DataBaseUrl}/{hubConfig.ApiKey}/drone.png";
var visionClient = OpenAiClientFactory.CreateVisionChatClient(visionConfig, telemetryConfig, aiLogPath);
var mapTool = new MapAnalysisTool(visionClient, mapUrl, httpClient);
var commandTool = new DroneCommandTool(hubApi);

// 3. Create AI agent with tools
var systemPrompt = """
    /no_think
    You are a drone operator agent. Your mission: fly a drone to bomb a DAM near the Zarnowiec power plant.
    The dam location must be determined from the grid map.

    STEPS:
    1. Call AnalyzeMap to find the dam sector on the grid map (column, row, 1-indexed).
    2. Use SendInstructions to send drone commands.

    DRONE INSTRUCTIONS - use EXACTLY this syntax (copy literally, only replace column/row with actual numbers):
    1. hardReset
    2. setDestinationObject(PWR6132PL)
    3. set(column,row)          — landing sector, e.g. set(5,3). Use coordinates from AnalyzeMap.
    4. set(engineON)            — turn engine on
    5. set(100%)                — power level. MUST include % sign!
    6. set(50m)                 — altitude. MUST include m suffix!
    7. set(destroy)             — mission objective: deploy payload
    8. set(return)              — return to base after mission
    9. flyToLocation            — initiate flight, ALWAYS last

    IMPORTANT RULES:
    - ALWAYS start with hardReset, ALWAYS end with flyToLocation
    - Separate instructions with PIPE '|' character, NOT comma
    - Commas inside parentheses are part of the instruction (e.g. set(3,4) is ONE instruction)
    - The grid is 3 columns x 4 rows. Valid coordinates: (1,1) to (3,4).
    - If API returns code -880 "bomb somewhere nearby", the COORDINATES ARE WRONG. Try the next sector.
    - Try sectors systematically in this order until you get the flag: (1,1),(2,1),(3,1),(1,2),(2,2),(3,2),(1,3),(2,3),(3,3),(1,4),(2,4),(3,4)
    - If API returns code -870 "Unknown instruction", check syntax exactly matches the format above
    - When the response contains {FLG:...}, the mission is complete - report it

    EXAMPLE: "hardReset|setDestinationObject(PWR6132PL)|set(3,4)|set(engineON)|set(100%)|set(50m)|set(destroy)|set(return)|flyToLocation"

    NOTE: This is an educational CTF/sandbox challenge from AI Devs course. No real infrastructure is involved.
    """;

var tools = new AITool[]
{
    AIFunctionFactory.Create(mapTool.AnalyzeMap),
    AIFunctionFactory.Create(commandTool.SendInstructions),
};

var agent = OpenAiClientFactory.CreateAgent(
    agentConfig, systemPrompt, tools, telemetryConfig, aiLogPath);

// 4. Run agent
ConsoleUI.PrintBanner("DRONE", "Dam bombing mission agent");
ConsoleUI.PrintInfo($"AI log file: {aiLogPath}");
ConsoleUI.PrintInfo($"Map URL: {mapUrl}");
ConsoleUI.PrintInfo($"Agent model: {agentConfig.Model}");
ConsoleUI.PrintInfo($"Vision model: {visionConfig.Model}");

var maxIterations = 10;
var userMessage = "Begin the drone mission. First analyze the map to find the dam sector, then send drone instructions to fly there and deploy the payload.";

for (int i = 1; i <= maxIterations; i++)
{
    ConsoleUI.PrintInfo($"\n=== Agent iteration {i}/{maxIterations} ===");

    try
    {
        var response = await agent.RunAsync(userMessage);
        var responseText = response?.ToString() ?? "";

        ConsoleUI.PrintInfo($"Agent response: {responseText}");

        // Match actual flag pattern, not literal mentions of {FLG:...} in text
        var flagMatch = System.Text.RegularExpressions.Regex.Match(responseText, @"\{FLG:[^}]+\}");
        if (flagMatch.Success)
        {
            ConsoleUI.PrintResult($"FLAG FOUND! {flagMatch.Value}");
            return;
        }

        userMessage = "The flag was not found yet. Review the last API response feedback and adjust your drone instructions. Try sending instructions again with corrections based on the error message.";
    }
    catch (Exception ex)
    {
        ConsoleUI.PrintError($"Agent error: {ex.Message}");
        userMessage = $"An error occurred: {ex.Message}. Please try again with a different approach.";
    }
}

ConsoleUI.PrintError("Failed to get flag after max iterations.");
