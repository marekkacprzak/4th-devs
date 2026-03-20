using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using PeopleAgent.Adapters;
using PeopleAgent.Config;
using PeopleAgent.Models;
using PeopleAgent.Services;
using PeopleAgent.Tools;
using PeopleAgent.Telemetry;
using PeopleAgent.UI;

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
var csvService = new CsvService(httpClient);
var taggingTools = new TaggingTools();

// 3. Run pipeline
ConsoleUI.PrintBanner("PEOPLE", "Hybrid pipeline + Agent Framework tagging");

// --- STEP 1: Download CSV ---
ConsoleUI.PrintStep("STEP 1: Download people.csv");
var csvUrl = $"{hubConfig.DataBaseUrl}/{hubConfig.ApiKey}/people.csv";
var csvContent = await csvService.DownloadCsvAsync(csvUrl);

// Show first few lines
var previewLines = csvContent.Split('\n').Take(5);
foreach (var line in previewLines)
    ConsoleUI.PrintInfo(line.Trim());

// --- STEP 2: Parse CSV ---
ConsoleUI.PrintStep("STEP 2: Parse CSV");
var allPeople = csvService.ParseCsv(csvContent);
ConsoleUI.PrintInfo($"Total people in CSV: {allPeople.Count}");

// --- STEP 3: Filter by criteria ---
ConsoleUI.PrintStep("STEP 3: Filter (M, Grudziądz, born 1986-2006)");
var filtered = allPeople
    .Where(p => p.Gender.Equals("M", StringComparison.OrdinalIgnoreCase))
    .Where(p => p.City.Equals("Grudziądz", StringComparison.OrdinalIgnoreCase))
    .Where(p => p.Born >= 1986 && p.Born <= 2006)
    .ToList();

ConsoleUI.PrintInfo($"After filtering: {filtered.Count} people");
foreach (var p in filtered)
    ConsoleUI.PrintInfo($"  {p.Name} {p.Surname} ({p.Born}, {p.City}) - {p.Job}");

if (filtered.Count == 0)
{
    ConsoleUI.PrintError("No people match the criteria. Check CSV format and column mapping.");
    return;
}

// --- STEP 4: Tag jobs using AIAgent ---
ConsoleUI.PrintStep("STEP 4: Tag jobs via AIAgent with TaggingTools");

// Build numbered job list
var jobsList = string.Join("\n", filtered.Select((p, i) => $"{i + 1}. {p.Job}"));
ConsoleUI.PrintInfo($"Jobs to classify:\n{jobsList}");

// Try AIAgent with tool calling first
var tagResults = new List<TagResult>();

ConsoleUI.PrintInfo("Attempting AIAgent with tool calling...");
try
{
    var tools = new List<AITool>
    {
        AIFunctionFactory.Create(taggingTools.SaveTagResults)
    };

    var agent = OpenAiClientFactory.CreateAgent(
        agentConfig,
        TaggingTools.BuildTaggingInstructions(),
        tools,
        telemetryConfig);

    var agentPrompt = $"Classify the following {filtered.Count} job descriptions and call SaveTagResults with the results:\n\n{jobsList}";
    var agentResponse = await agent.RunAsync(agentPrompt);
    ConsoleUI.PrintInfo($"Agent response: {agentResponse.Text}");

    tagResults = taggingTools.GetResults();
    ConsoleUI.PrintInfo($"Tag results from tool call: {tagResults.Count}");
}
catch (Exception ex)
{
    ConsoleUI.PrintInfo($"Agent tool calling failed: {ex.Message}");
}

// Fallback: use ChatClient with JSON structured output
if (tagResults.Count == 0)
{
    ConsoleUI.PrintInfo("Falling back to ChatClient with Structured Output (JSON)...");
    var chatClient = OpenAiClientFactory.CreateChatClient(agentConfig, telemetryConfig);

    var taggingPrompt = TaggingTools.BuildTaggingInstructions() + "\n\n" +
        "IMPORTANT: Respond ONLY with a JSON object in this exact format, no other text:\n" +
        "{\"results\": [{\"id\": 1, \"tags\": [\"tag1\", \"tag2\"]}, ...]}\n\n" +
        $"Classify these {filtered.Count} job descriptions:\n\n{jobsList}";

    var chatResponse = await chatClient.GetResponseAsync(taggingPrompt);
    var responseText = chatResponse.Text ?? "";
    ConsoleUI.PrintInfo($"ChatClient response: {responseText[..Math.Min(responseText.Length, 500)]}");

    try
    {
        // Extract JSON from response
        var jsonStart = responseText.IndexOf('{');
        var jsonEnd = responseText.LastIndexOf('}');
        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            var jsonStr = responseText[jsonStart..(jsonEnd + 1)];
            var parsed = JsonSerializer.Deserialize<TagResultsWrapper>(jsonStr);
            if (parsed?.Results != null)
            {
                tagResults = parsed.Results;
                ConsoleUI.PrintInfo($"Parsed {tagResults.Count} results from structured output");
            }
        }
    }
    catch (Exception ex)
    {
        ConsoleUI.PrintError($"Failed to parse JSON response: {ex.Message}");
    }
}

if (tagResults.Count == 0)
{
    ConsoleUI.PrintError("Could not get tag results. Aborting.");
    return;
}

// Normalize tags: map aliases (logistyka→transport) and remove invalid tags
tagResults = TaggingTools.NormalizeTags(tagResults);
ConsoleUI.PrintInfo("Tags normalized (aliases mapped, invalid removed)");

// Assign tags to people
for (int i = 0; i < filtered.Count; i++)
{
    var result = tagResults.FirstOrDefault(r => r.Id == i + 1);
    if (result != null)
        filtered[i].Tags = result.Tags;
}

// Show all tagged people
ConsoleUI.PrintInfo("Tagged results:");
foreach (var p in filtered)
    ConsoleUI.PrintInfo($"  {p.Name} {p.Surname}: [{string.Join(", ", p.Tags)}]");

// --- STEP 5: Filter by transport tag ---
ConsoleUI.PrintStep("STEP 5: Filter by 'transport' tag");
var transportPeople = filtered
    .Where(p => p.Tags.Any(t => t.Equals("transport", StringComparison.OrdinalIgnoreCase)))
    .ToList();

ConsoleUI.PrintInfo($"People with 'transport' tag: {transportPeople.Count}");
foreach (var p in transportPeople)
    ConsoleUI.PrintInfo($"  {p.Name} {p.Surname} ({p.Born}, {p.City}) - {p.Job} -> [{string.Join(", ", p.Tags)}]");

if (transportPeople.Count == 0)
{
    ConsoleUI.PrintError("No people with 'transport' tag found. Check LLM classification.");
    return;
}

// --- STEP 5b: Export suspects for next task ---
ConsoleUI.PrintStep("STEP 5b: Export suspects to JSON");
var suspects = transportPeople.Select(p => new { p.Name, p.Surname, BirthYear = p.Born }).ToList();
var suspectsJson = JsonSerializer.Serialize(suspects, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
var exportPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "exam_01_02_task_agent", "suspects.json"));
await File.WriteAllTextAsync(exportPath, suspectsJson);
ConsoleUI.PrintInfo($"Exported {suspects.Count} suspects to {exportPath}");

// --- STEP 6: Submit to Hub ---
ConsoleUI.PrintStep("STEP 6: Submit to Hub API");

var jsonPayload = JsonSerializer.Serialize(transportPeople, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true
});
ConsoleUI.PrintInfo($"Payload ({transportPeople.Count} people):\n{jsonPayload}");

var response = await hubApi.SubmitPeopleAsync(transportPeople);

if (response.Contains("{FLG:"))
{
    ConsoleUI.PrintResult($"SUCCESS! {response}");
}
else
{
    ConsoleUI.PrintError($"Submission failed: {response}");
}
