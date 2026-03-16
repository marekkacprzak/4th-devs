using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using RailwayAgent.Adapters;
using RailwayAgent.Config;
using RailwayAgent.Services;
using RailwayAgent.Tools;
using RailwayAgent.Telemetry;
using RailwayAgent.UI;

// 1. Load configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var agentConfig = new AgentConfig();
configuration.GetSection("Agent").Bind(agentConfig);

var railwayConfig = new RailwayConfig();
configuration.GetSection("Railway").Bind(railwayConfig);

var telemetryConfig = new TelemetryConfig();
configuration.GetSection("Telemetry").Bind(telemetryConfig);

using var telemetry = new TelemetrySetup(telemetryConfig);

// 2. Create services
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var railwayApi = new RailwayApiClient(httpClient, railwayConfig);

// 3. Create tools
var basePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
var railwayTools = new RailwayApiTools(railwayApi);
var fileTools = new FileTools(basePath);

var tools = new List<AITool>
{
    AIFunctionFactory.Create(railwayTools.Help),
    AIFunctionFactory.Create(railwayTools.Reconfigure),
    AIFunctionFactory.Create(railwayTools.GetStatus),
    AIFunctionFactory.Create(railwayTools.SetStatus),
    AIFunctionFactory.Create(railwayTools.Save),
    AIFunctionFactory.Create(fileTools.ReadFile),
};

// 4. Create agent
var instructions = """
    You are a railway route management agent. Your task is to activate railway route X-01.

    Available local files (use ReadFile tool):
    - trasy_wylaczone.csv - list of disabled railway routes
    - help_answer.json - API documentation

    Your goal: Change the status of route X-01 from RTCLOSE to RTOPEN.

    Required workflow (execute in this exact order):
    1. Call Reconfigure with route "x-01" to enter reconfigure mode
    2. Call SetStatus with route "x-01" and value "RTOPEN" to set the status
    3. Call Save with route "x-01" to save and exit reconfigure mode

    Important:
    - If any step fails, read the error message carefully and retry
    - Route format is lowercase: x-01
    - After Save, look for a flag in format {FLG:...} in the response
    - Report the flag when you find it
    """;

var agent = OpenAiClientFactory.CreateAgent(agentConfig, instructions, tools, telemetryConfig);

// 5. Run agent
ConsoleUI.PrintBanner("RAILWAY", $"Provider: {agentConfig.Provider} | Model: {agentConfig.Model}");

var result = await agent.RunAsync("Activate railway route X-01 by changing its status to RTOPEN. Follow the workflow: reconfigure -> setstatus -> save.");

ConsoleUI.PrintResult(result.ToString());
