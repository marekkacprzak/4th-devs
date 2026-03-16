using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using CategorizeAgent.Adapters;
using CategorizeAgent.Config;
using CategorizeAgent.Services;
using CategorizeAgent.Tools;
using CategorizeAgent.UI;

// 1. Load configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var agentConfig = new AgentConfig();
configuration.GetSection("Agent").Bind(agentConfig);

var hubConfig = new HubConfig();
configuration.GetSection("Hub").Bind(hubConfig);

// 2. Create services
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var hubApi = new HubApiClient(httpClient, hubConfig);
var csvService = new CsvService();
var categorizationTools = new CategorizationTools(hubApi, csvService);

// 3. Create AI agent with tools
ConsoleUI.PrintBanner("CATEGORIZE", "Prompt engineer agent for goods classification");

var tools = new List<AITool>
{
    AIFunctionFactory.Create(categorizationTools.RunClassificationCycle)
};

var systemPrompt = CategorizationTools.BuildSystemPrompt();
var agent = OpenAiClientFactory.CreateAgent(agentConfig, systemPrompt, tools);

// 4. Run agent loop
const int maxAttempts = 10;
var userMessage = "Craft a classification prompt template and test it by calling RunClassificationCycle. Iterate until you get the {FLG:...} flag. Start now.";

for (int attempt = 1; attempt <= maxAttempts; attempt++)
{
    ConsoleUI.PrintStep($"Agent attempt {attempt}/{maxAttempts}");

    try
    {
        var response = await agent.RunAsync(userMessage);
        var responseText = response.Text ?? "";

        ConsoleUI.PrintInfo($"Agent response: {responseText}");

        if (responseText.Contains("{FLG:"))
        {
            ConsoleUI.PrintResult($"SUCCESS! Flag found: {responseText}");
            return;
        }

        // Feed result back for next attempt
        userMessage = $"Previous attempt did not yield the flag. Result:\n{responseText}\n\nPlease refine your prompt template and call RunClassificationCycle again. Analyze what went wrong and adjust.";
    }
    catch (Exception ex)
    {
        ConsoleUI.PrintError($"Agent error: {ex.Message}");
        userMessage = $"An error occurred: {ex.Message}\nPlease try again with a different approach.";
    }
}

ConsoleUI.PrintError($"Failed to obtain flag after {maxAttempts} attempts.");
