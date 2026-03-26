using Negotiations.Config;
using Negotiations.UI;

namespace Negotiations.Services;

public class CsvDataService
{
    private readonly HttpClient _httpClient;
    private readonly HubConfig _hubConfig;

    // city code <-> name
    private Dictionary<string, string> _cityCodeToName = new();
    private Dictionary<string, string> _cityNameToCode = new();

    // item code <-> name
    private Dictionary<string, string> _itemCodeToName = new();
    // lowercase item name -> code (for exact matching)
    private Dictionary<string, string> _itemNameToCode = new();

    // itemCode -> list of cityCode
    private Dictionary<string, List<string>> _itemCodeToCityCodes = new();

    public bool IsLoaded { get; private set; }

    public CsvDataService(HttpClient httpClient, HubConfig hubConfig)
    {
        _httpClient = httpClient;
        _hubConfig = hubConfig;
    }

    public async Task LoadAsync()
    {
        ConsoleUI.PrintStep("Loading CSV data...");

        var citiesCsv = await _httpClient.GetStringAsync(_hubConfig.CsvBaseUrl + "cities.csv");
        var itemsCsv = await _httpClient.GetStringAsync(_hubConfig.CsvBaseUrl + "items.csv");
        var connectionsCsv = await _httpClient.GetStringAsync(_hubConfig.CsvBaseUrl + "connections.csv");

        ParseCities(citiesCsv);
        ParseItems(itemsCsv);
        ParseConnections(connectionsCsv);

        IsLoaded = true;

        ConsoleUI.PrintInfo($"Loaded {_cityCodeToName.Count} cities, {_itemCodeToName.Count} items, {_itemCodeToCityCodes.Count} item-city mappings");
    }

    private void ParseCities(string csv)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines.Skip(1)) // skip header
        {
            var parts = line.Trim().Split(',');
            if (parts.Length < 2) continue;
            var name = parts[0].Trim();
            var code = parts[1].Trim();
            _cityCodeToName[code] = name;
            _cityNameToCode[name.ToLowerInvariant()] = code;
        }
    }

    private void ParseItems(string csv)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Trim().Split(',');
            if (parts.Length < 2) continue;
            var name = parts[0].Trim();
            var code = parts[1].Trim();
            _itemCodeToName[code] = name;
            _itemNameToCode[name.ToLowerInvariant()] = code;
        }
    }

    private void ParseConnections(string csv)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Trim().Split(',');
            if (parts.Length < 2) continue;
            var itemCode = parts[0].Trim();
            var cityCode = parts[1].Trim();

            if (!_itemCodeToCityCodes.TryGetValue(itemCode, out var cities))
            {
                cities = new List<string>();
                _itemCodeToCityCodes[itemCode] = cities;
            }
            cities.Add(cityCode);
        }
    }

    public List<string> GetCitiesForItemCode(string itemCode)
    {
        if (!_itemCodeToCityCodes.TryGetValue(itemCode, out var cityCodes))
            return [];

        return cityCodes
            .Select(code => _cityCodeToName.TryGetValue(code, out var name) ? name : null)
            .Where(n => n is not null)
            .Cast<string>()
            .Distinct()
            .OrderBy(n => n)
            .ToList();
    }

    public string? FindItemCodeByName(string normalizedName)
    {
        return _itemNameToCode.TryGetValue(normalizedName, out var code) ? code : null;
    }

    public List<(string Name, string Code)> FindItemsByKeywords(IEnumerable<string> keywords)
    {
        var result = new List<(string Name, string Code)>();
        foreach (var (name, code) in _itemNameToCode)
        {
            if (keywords.Any(kw => name.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                result.Add((name, code));
        }
        return result;
    }

    public List<string> GetAllItemNames()
    {
        return _itemCodeToName.Values.OrderBy(n => n).ToList();
    }
}
