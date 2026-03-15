using System.ComponentModel;
using System.Text.Json;
using FindHimAgent.Config;
using FindHimAgent.Models;
using FindHimAgent.Services;
using FindHimAgent.UI;

namespace FindHimAgent.Tools;

public class InvestigationTools
{
    private readonly HubApiClient _hubApi;
    private readonly HubConfig _hubConfig;
    private string? _cachedPowerPlants;

    public InvestigationTools(HubApiClient hubApi, HubConfig hubConfig)
    {
        _hubApi = hubApi;
        _hubConfig = hubConfig;
    }

    [Description("Get the list of suspects from the previous task. Returns names, surnames and birth years.")]
    public string GetSuspects()
    {
        ConsoleUI.PrintToolCall("GetSuspects");
        try
        {
            var json = File.ReadAllText("suspects.json");
            var suspects = JsonSerializer.Deserialize<List<Suspect>>(json);
            if (suspects == null || suspects.Count == 0)
                return "ERROR: No suspects found in suspects.json";

            var lines = suspects.Select((s, i) => $"{i + 1}. {s.Name} {s.Surname} (born {s.BirthYear})");
            var result = $"Found {suspects.Count} suspects:\n{string.Join("\n", lines)}";
            ConsoleUI.PrintInfo(result);
            return result;
        }
        catch (Exception ex)
        {
            return $"ERROR reading suspects.json: {ex.Message}";
        }
    }

    [Description("Get GPS locations where a specific person was seen. Returns list of latitude/longitude coordinates.")]
    public async Task<string> GetPersonLocations(
        [Description("First name of the person")] string name,
        [Description("Surname of the person")] string surname)
    {
        ConsoleUI.PrintToolCall("GetPersonLocations", $"{name} {surname}");
        var response = await _hubApi.GetPersonLocationsAsync(name, surname);
        ConsoleUI.PrintInfo($"Locations for {name} {surname}: {response}");
        return response;
    }

    [Description("Get list of nuclear power plants with their codes and GPS coordinates.")]
    public async Task<string> GetPowerPlants()
    {
        ConsoleUI.PrintToolCall("GetPowerPlants");
        if (_cachedPowerPlants != null)
        {
            ConsoleUI.PrintInfo("Returning cached power plants data");
            return _cachedPowerPlants;
        }

        var response = await _hubApi.GetPowerPlantsAsync();
        _cachedPowerPlants = response;
        ConsoleUI.PrintInfo($"Power plants: {response}");
        return response;
    }

    [Description("Calculate the distance in kilometers between two GPS coordinates using the Haversine formula.")]
    public string CalculateDistance(
        [Description("Latitude of point 1")] double lat1,
        [Description("Longitude of point 1")] double lon1,
        [Description("Latitude of point 2")] double lat2,
        [Description("Longitude of point 2")] double lon2)
    {
        ConsoleUI.PrintToolCall("CalculateDistance", $"({lat1}, {lon1}) -> ({lat2}, {lon2})");

        const double R = 6371.0; // Earth radius in km
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        var distance = R * c;

        var result = $"Distance: {distance:F2} km";
        ConsoleUI.PrintInfo(result);
        return result;
    }

    [Description("Get the access level of a person. Requires name, surname, and birth year (integer).")]
    public async Task<string> GetAccessLevel(
        [Description("First name")] string name,
        [Description("Surname")] string surname,
        [Description("Birth year as integer, e.g. 1987")] int birthYear)
    {
        ConsoleUI.PrintToolCall("GetAccessLevel", $"{name} {surname}, born {birthYear}");
        var response = await _hubApi.GetAccessLevelAsync(name, surname, birthYear);
        ConsoleUI.PrintInfo($"Access level for {name} {surname}: {response}");
        return response;
    }

    [Description("Submit the final answer identifying the suspect near the power plant.")]
    public async Task<string> SubmitAnswer(
        [Description("First name of the suspect")] string name,
        [Description("Surname of the suspect")] string surname,
        [Description("Access level (integer)")] int accessLevel,
        [Description("Power plant code in PWR0000PL format")] string powerPlant)
    {
        ConsoleUI.PrintToolCall("SubmitAnswer", $"{name} {surname}, level={accessLevel}, plant={powerPlant}");
        var answer = new
        {
            name,
            surname,
            accessLevel,
            powerPlant
        };
        var response = await _hubApi.SubmitAnswerAsync(answer);
        ConsoleUI.PrintInfo($"Submit response: {response}");
        return response;
    }

    public static string BuildSystemPrompt()
    {
        return """
            You are an investigation agent. Your task is to find which suspect was seen near a nuclear power plant.

            Follow these steps IN ORDER:
            1. Call GetSuspects() to get the list of suspects with their names and birth years.
            2. Call GetPowerPlants() to get all power plant locations with their codes and GPS coordinates.
            3. For EACH suspect, call GetPersonLocations(name, surname) to get their GPS coordinates where they were seen.
            4. For each suspect's location, call CalculateDistance(lat1, lon1, lat2, lon2) comparing against each power plant location to find the minimum distance.
            5. The suspect whose location is closest to any power plant is the target. Note which power plant code (PWR format) they were near.
            6. For that suspect, call GetAccessLevel(name, surname, birthYear) to get their access level.
            7. Finally, call SubmitAnswer(name, surname, accessLevel, powerPlant) with all the collected data.

            IMPORTANT RULES:
            - Process ALL suspects before deciding who is closest.
            - Track the minimum distance found so far across all suspects and all their locations.
            - The power plant code must be in PWR0000PL format (e.g. PWR1234PL).
            - birthYear must be an integer (e.g. 1987).
            - Call exactly one tool at a time and wait for the result before proceeding.
            - Parse the JSON responses carefully to extract latitude and longitude values.
            """;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
