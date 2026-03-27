using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SavethemAgent.Models;
using SavethemAgent.Services;
using SavethemAgent.UI;

namespace SavethemAgent.Tools;

public class SavethemTools
{
    private readonly HubApiClient _hubApi;
    private readonly RoutePlanner _routePlanner;

    private GridMap? _currentMap;
    private readonly List<Vehicle> _vehicles = new();
    private RouteResult? _currentRoute;
    private string _lastVerifyResponse = "";

    public SavethemTools(HubApiClient hubApi, RoutePlanner routePlanner)
    {
        _hubApi = hubApi;
        _routePlanner = routePlanner;
    }

    // ── Tool 1: Search for available tools ───────────────────────────────────

    [Description("Search for available tools using the tool search API. Use English queries. Returns top 3 tools with their names, URLs, and descriptions. Example queries: 'terrain map grid', 'vehicle transport fuel', 'movement rules'.")]
    public async Task<string> SearchAvailableTools(
        [Description("Natural language query in English to find relevant tools")] string query)
    {
        ConsoleUI.PrintToolCall("SearchAvailableTools", query);
        var response = await _hubApi.ToolSearchAsync(query);
        ConsoleUI.PrintInfo($"Tool search result: {Truncate(response, 300)}");
        return response;
    }

    // ── Tool 2: Call a discovered tool ───────────────────────────────────────

    [Description("Call a discovered tool URL to get information. All tools accept a 'query' parameter in English and return JSON with top 3 matching results. Use this to get the map, vehicle data, movement rules, etc.")]
    public async Task<string> CallTool(
        [Description("The full URL of the tool to call (from SearchAvailableTools results)")] string toolUrl,
        [Description("Query to send to the tool in English")] string query)
    {
        ConsoleUI.PrintToolCall("CallTool", $"{toolUrl} | {query}");
        var response = await _hubApi.CallToolAsync(toolUrl, query);
        ConsoleUI.PrintInfo($"Tool response: {Truncate(response, 400)}");
        return response;
    }

    // ── Tool 3: Parse and store map ──────────────────────────────────────────

    [Description("Parse and store the terrain map from the tool response JSON. Call this after getting map data from CallTool. Returns a summary of the parsed map including start position, goal position, and obstacle count.")]
    public string ParseAndStoreMap(
        [Description("The raw JSON response from the map tool")] string mapJson)
    {
        ConsoleUI.PrintToolCall("ParseAndStoreMap");
        _currentMap = MapParser.Parse(mapJson);

        int obstacles = 0;
        for (int r = 0; r < GridMap.Height; r++)
            for (int c = 0; c < GridMap.Width; c++)
                if (_currentMap.Cells[r, c] == CellType.Obstacle)
                    obstacles++;

        var (sr, sc) = _currentMap.StartPosition;
        var (gr, gc) = _currentMap.GoalPosition;
        var grid = _currentMap.ToTextGrid();

        ConsoleUI.PrintBoard(grid);
        return $"Map stored: {GridMap.Width}x{GridMap.Height}, start=({sr},{sc}), goal=({gr},{gc}), obstacles={obstacles}\n\nGrid:\n{grid}";
    }

    // ── Tool 4: Parse and store vehicles ────────────────────────────────────

    [Description("Parse and store vehicle data from the tool response JSON. ADDITIVE — call once per vehicle to accumulate all vehicles. Call this after each CallTool response for a vehicle. Returns a summary of all stored vehicles so far.")]
    public string ParseAndStoreVehicles(
        [Description("The raw JSON response from the vehicles tool (one vehicle at a time)")] string vehiclesJson)
    {
        ConsoleUI.PrintToolCall("ParseAndStoreVehicles");
        // Additive: do not clear — accumulate all vehicles across multiple calls
        var parsed = VehicleParser.Parse(vehiclesJson);
        foreach (var v in parsed)
        {
            if (!_vehicles.Any(x => x.Name.Equals(v.Name, StringComparison.OrdinalIgnoreCase)))
                _vehicles.Add(v);
        }

        if (_vehicles.Count == 0)
        {
            return "WARNING: No vehicles parsed. Make sure the JSON contains vehicle data with names and fuel consumption.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Vehicles stored ({_vehicles.Count}):");
        foreach (var v in _vehicles)
            sb.AppendLine($"  - {v}");

        var result = sb.ToString();
        ConsoleUI.PrintInfo(result);
        return result;
    }

    // ── Tool 5: Plan optimal route ───────────────────────────────────────────

    [Description("Calculate the optimal route using BFS pathfinding with resource constraints (10 food, 10 fuel). Supports hybrid vehicle switching (e.g. rocket on land, walk through water). Requires map and vehicles to be stored first. Returns vehicle segments and move sequence.")]
    public string PlanOptimalRoute()
    {
        ConsoleUI.PrintToolCall("PlanOptimalRoute");

        if (_currentMap == null)
            return "ERROR: No map stored. Call ParseAndStoreMap first.";

        if (_vehicles.Count == 0)
            return "ERROR: No vehicles stored. Call ParseAndStoreVehicles first.";

        _currentRoute = _routePlanner.Plan(_currentMap, _vehicles);

        if (!_currentRoute.IsValid)
        {
            ConsoleUI.PrintError(_currentRoute.ErrorMessage ?? "Route planning failed");
            return $"Route planning failed: {_currentRoute.ErrorMessage}";
        }

        var answer = _currentRoute.ToAnswer();
        var result = $"{_currentRoute}\nAnswer: [{string.Join(", ", answer)}]";
        ConsoleUI.PrintInfo(result);
        return result;
    }

    // ── Tool 6: Submit solution ──────────────────────────────────────────────

    [Description("Submit the planned route to the verification endpoint. Call this after PlanOptimalRoute succeeds. Returns the verification result including the flag if the route is correct.")]
    public async Task<string> SubmitSolution()
    {
        ConsoleUI.PrintToolCall("SubmitSolution");

        if (_currentRoute == null || !_currentRoute.IsValid)
            return "ERROR: No valid route planned. Call PlanOptimalRoute first.";

        var answer = _currentRoute.ToAnswer();
        ConsoleUI.PrintInfo($"Submitting: [{string.Join(", ", answer)}]");

        _lastVerifyResponse = await _hubApi.VerifyAsync(answer);
        ConsoleUI.PrintInfo($"Verify response: {_lastVerifyResponse}");
        return _lastVerifyResponse;
    }

    // ── Public state accessors ────────────────────────────────────────────────

    public RouteResult? CurrentRoute => _currentRoute;
    public GridMap? CurrentMap => _currentMap;
    public IReadOnlyList<Vehicle> Vehicles => _vehicles;
    public string LastVerifyResponse => _lastVerifyResponse;

    // ── Tool registration ─────────────────────────────────────────────────────

    public IEnumerable<AIFunction> GetAIFunctions()
    {
        yield return AIFunctionFactory.Create(SearchAvailableTools);
        yield return AIFunctionFactory.Create(CallTool);
        yield return AIFunctionFactory.Create((Func<string, string>)ParseAndStoreMap);
        yield return AIFunctionFactory.Create((Func<string, string>)ParseAndStoreVehicles);
        yield return AIFunctionFactory.Create((Func<string>)PlanOptimalRoute);
        yield return AIFunctionFactory.Create(SubmitSolution);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";
}
