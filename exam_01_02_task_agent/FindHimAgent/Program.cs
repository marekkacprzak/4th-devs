using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using FindHimAgent.Adapters;
using FindHimAgent.Config;
using FindHimAgent.Models;
using FindHimAgent.Services;
using FindHimAgent.Tools;
using FindHimAgent.Telemetry;
using FindHimAgent.UI;

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

var telemetryConfig = new TelemetryConfig();
configuration.GetSection("Telemetry").Bind(telemetryConfig);

using var telemetry = new TelemetrySetup(telemetryConfig);

// 2. Create services
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var hubApi = new HubApiClient(httpClient, hubConfig);
var investigationTools = new InvestigationTools(hubApi, hubConfig);

// 3. Known GPS coordinates for power plant cities
var cityCoordinates = new Dictionary<string, (double lat, double lon)>(StringComparer.OrdinalIgnoreCase)
{
    ["Zabrze"] = (50.3249, 18.7857),
    ["Piotrków Trybunalski"] = (51.4053, 19.7031),
    ["Grudziądz"] = (53.4837, 18.7536),
    ["Tczew"] = (54.0927, 18.7953),
    ["Radom"] = (51.4027, 21.1471),
    ["Chelmno"] = (53.3489, 18.4257),
    ["Żarnowiec"] = (54.7831, 18.1345),
};

// 4. Run
ConsoleUI.PrintBanner("FINDHIM", "Find suspect near nuclear power plant");

// --- STEP 1: Load suspects ---
ConsoleUI.PrintStep("STEP 1: Load suspects");
var suspectsJson = File.ReadAllText("suspects.json");
var suspects = JsonSerializer.Deserialize<List<Suspect>>(suspectsJson)!;
ConsoleUI.PrintInfo($"Loaded {suspects.Count} suspects:");
foreach (var s in suspects)
    ConsoleUI.PrintInfo($"  {s.Name} {s.Surname} (born {s.BirthYear})");

// --- STEP 2: Get power plants ---
ConsoleUI.PrintStep("STEP 2: Get power plants");
var plantsResponse = await hubApi.GetPowerPlantsAsync();
var plants = ParsePowerPlants(plantsResponse, cityCoordinates);
ConsoleUI.PrintInfo($"Parsed {plants.Count} power plants:");
foreach (var p in plants)
    ConsoleUI.PrintInfo($"  {p.Code} at ({p.Latitude:F4}, {p.Longitude:F4})");

// --- STEP 3: Get locations for each suspect and find closest ---
ConsoleUI.PrintStep("STEP 3: Find closest suspect to any power plant");

string? closestName = null, closestSurname = null;
int closestBirthYear = 0;
double minDistance = double.MaxValue;
string? closestPlantCode = null;

foreach (var suspect in suspects)
{
    ConsoleUI.PrintInfo($"Checking {suspect.Name} {suspect.Surname}...");
    var locResponse = await hubApi.GetPersonLocationsAsync(suspect.Name, suspect.Surname);

    var locations = ParseLocations(locResponse);
    ConsoleUI.PrintInfo($"  Found {locations.Count} locations");

    foreach (var (lat, lon) in locations)
    {
        foreach (var plant in plants)
        {
            var dist = Haversine(lat, lon, plant.Latitude, plant.Longitude);
            if (dist < minDistance)
            {
                minDistance = dist;
                closestName = suspect.Name;
                closestSurname = suspect.Surname;
                closestBirthYear = suspect.BirthYear;
                closestPlantCode = plant.Code;
                ConsoleUI.PrintInfo($"  New closest: {dist:F2} km to {plant.Code}");
            }
        }
    }
}

if (closestName == null)
{
    ConsoleUI.PrintError("Could not find any suspect near a power plant");
    return;
}

ConsoleUI.PrintStep($"RESULT: {closestName} {closestSurname} at {minDistance:F2} km from {closestPlantCode}");

// --- STEP 4: Get access level ---
ConsoleUI.PrintStep("STEP 4: Get access level");
var accessResponse = await hubApi.GetAccessLevelAsync(closestName, closestSurname, closestBirthYear);
var accessLevel = ParseAccessLevel(accessResponse);
ConsoleUI.PrintInfo($"Access level: {accessLevel}");

// --- STEP 5: Submit answer ---
ConsoleUI.PrintStep("STEP 5: Submit answer");
var answer = new
{
    name = closestName,
    surname = closestSurname,
    accessLevel,
    powerPlant = closestPlantCode
};
ConsoleUI.PrintInfo($"Answer: {JsonSerializer.Serialize(answer, new JsonSerializerOptions { WriteIndented = true })}");

var result = await hubApi.SubmitAnswerAsync(answer);

if (result.Contains("{FLG:"))
{
    ConsoleUI.PrintResult($"SUCCESS! {result}");
}
else
{
    ConsoleUI.PrintError($"Submission result: {result}");
}

// --- Helper functions ---

double Haversine(double lat1, double lon1, double lat2, double lon2)
{
    const double R = 6371.0;
    var dLat = ToRadians(lat2 - lat1);
    var dLon = ToRadians(lon2 - lon1);
    var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
            Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
    var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    return R * c;
}

double ToRadians(double degrees) => degrees * Math.PI / 180.0;

List<PowerPlant> ParsePowerPlants(string json, Dictionary<string, (double lat, double lon)> coords)
{
    var plants = new List<PowerPlant>();
    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;

    // Format: { "power_plants": { "CityName": { "code": "PWR...", ... }, ... } }
    JsonElement plantsObj;
    if (root.TryGetProperty("power_plants", out var pp))
        plantsObj = pp;
    else
        plantsObj = root;

    foreach (var prop in plantsObj.EnumerateObject())
    {
        var cityName = prop.Name;
        var code = "";

        if (prop.Value.TryGetProperty("code", out var codeProp))
            code = codeProp.GetString() ?? "";

        if (coords.TryGetValue(cityName, out var coord))
        {
            plants.Add(new PowerPlant
            {
                Code = code,
                Latitude = coord.lat,
                Longitude = coord.lon
            });
        }
        else
        {
            ConsoleUI.PrintInfo($"  WARNING: No coordinates for city '{cityName}', skipping");
        }
    }

    return plants;
}

List<(double lat, double lon)> ParseLocations(string json)
{
    var locations = new List<(double, double)>();

    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;

    // API returns: [ { "latitude": ..., "longitude": ... }, ... ]
    JsonElement arrayElem;
    if (root.ValueKind == JsonValueKind.Array)
        arrayElem = root;
    else if (root.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.Array)
        arrayElem = msgProp;
    else
        return locations;

    foreach (var item in arrayElem.EnumerateArray())
    {
        double lat = 0, lon = 0;

        if (item.TryGetProperty("latitude", out var latP))
            lat = latP.GetDouble();
        else if (item.TryGetProperty("lat", out var lat2P))
            lat = lat2P.GetDouble();

        if (item.TryGetProperty("longitude", out var lonP))
            lon = lonP.GetDouble();
        else if (item.TryGetProperty("lon", out var lon2P))
            lon = lon2P.GetDouble();

        locations.Add((lat, lon));
    }

    return locations;
}

int ParseAccessLevel(string json)
{
    try
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("accessLevel", out var alProp))
            return alProp.GetInt32();
        if (root.TryGetProperty("access_level", out var al2Prop))
            return al2Prop.GetInt32();
        if (root.TryGetProperty("message", out var msgProp))
        {
            var msg = msgProp.ToString();
            if (int.TryParse(msg, out var parsed))
                return parsed;
            // Try extracting number from message string
            foreach (var word in msg.Split(' ', ',', '.', ':', '"'))
            {
                if (int.TryParse(word, out var wordParsed))
                    return wordParsed;
            }
        }
    }
    catch
    {
        if (int.TryParse(json.Trim(), out var plain))
            return plain;
    }

    return 0;
}
