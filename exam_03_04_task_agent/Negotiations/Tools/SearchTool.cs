using System.Text;
using Negotiations.Services;
using Negotiations.UI;

namespace Negotiations.Tools;

public class SearchTool
{
    private readonly CsvDataService _csvData;
    private readonly ItemMatcherService _matcher;
    private readonly InteractionLogger _logger;

    private const int MaxResponseBytes = 490;

    public SearchTool(CsvDataService csvData, ItemMatcherService matcher, InteractionLogger logger)
    {
        _csvData = csvData;
        _matcher = matcher;
        _logger = logger;
    }

    public async Task<string> SearchItemsAsync(string query)
    {
        ConsoleUI.PrintToolCall("SearchItems", query);

        var itemCode = await _matcher.MatchItemCodeAsync(query);
        if (itemCode is null)
        {
            var notFound = "Nie znaleziono pasującego przedmiotu w bazie danych.";
            ConsoleUI.PrintResult(notFound);
            return notFound;
        }

        var cities = _csvData.GetCitiesForItemCode(itemCode);
        if (cities.Count == 0)
        {
            var noCities = "Przedmiot znaleziony, ale żadne miasto go nie oferuje.";
            ConsoleUI.PrintResult(noCities);
            return noCities;
        }

        // Build response, respecting the 500-byte limit
        var result = BuildResponse(cities);
        ConsoleUI.PrintResult(result);
        await _logger.LogInfo($"SearchItems: '{query}' → itemCode={itemCode}, cities={result}");
        return result;
    }

    private static string BuildResponse(List<string> cities)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < cities.Count; i++)
        {
            var part = i == 0 ? cities[i] : ", " + cities[i];
            if (Encoding.UTF8.GetByteCount(sb.ToString() + part + ", i inne") > MaxResponseBytes)
            {
                sb.Append(", i inne");
                break;
            }
            sb.Append(part);
        }
        return sb.ToString();
    }
}
