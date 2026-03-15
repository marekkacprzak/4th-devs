using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using PeopleAgent.UI;

namespace PeopleAgent.Tools;

public class TagResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
}

public class TagResultsWrapper
{
    [JsonPropertyName("results")]
    public List<TagResult> Results { get; set; } = new();
}

public class TaggingTools
{
    private TagResultsWrapper? _lastResults;

    [Description("Save the batch tag classification results. Input must be a JSON object with a 'results' array, where each element has 'id' (int) and 'tags' (string array from: IT, transport, edukacja, medycyna, praca z ludźmi, praca z pojazdami, praca fizyczna).")]
    public string SaveTagResults(
        [Description("JSON string: {\"results\": [{\"id\": 1, \"tags\": [\"transport\"]}, ...]}")] string tagResultsJson)
    {
        ConsoleUI.PrintToolCall("SaveTagResults", $"{tagResultsJson.Length} chars");

        try
        {
            _lastResults = JsonSerializer.Deserialize<TagResultsWrapper>(tagResultsJson);
            var count = _lastResults?.Results?.Count ?? 0;
            ConsoleUI.PrintInfo($"Saved {count} tag results");
            return $"Successfully saved {count} tag results.";
        }
        catch (Exception ex)
        {
            ConsoleUI.PrintError($"Failed to parse tag results: {ex.Message}");
            return $"Error parsing JSON: {ex.Message}. Please send valid JSON with format: {{\"results\": [{{\"id\": 1, \"tags\": [\"tag1\"]}}]}}";
        }
    }

    public List<TagResult> GetResults() => _lastResults?.Results ?? new();

    private static readonly HashSet<string> ValidTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "IT", "transport", "edukacja", "medycyna",
        "praca z ludźmi", "praca z pojazdami", "praca fizyczna"
    };

    private static readonly Dictionary<string, string> TagAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["logistyka"] = "transport",
        ["logistics"] = "transport",
        ["spedycja"] = "transport",
        ["kierowca"] = "transport",
        ["przewozy"] = "transport",
        ["informatyka"] = "IT",
        ["programowanie"] = "IT",
        ["nauczyciel"] = "edukacja",
        ["lekarz"] = "medycyna",
        ["zdrowie"] = "medycyna",
        ["budownictwo"] = "praca fizyczna",
        ["magazyn"] = "praca fizyczna",
        ["produkcja"] = "praca fizyczna",
        ["mechanik"] = "praca z pojazdami",
        ["hr"] = "praca z ludźmi",
        ["sprzedaż"] = "praca z ludźmi",
    };

    /// <summary>
    /// Normalize and validate tags: map aliases to valid tags, remove invalid ones.
    /// </summary>
    public static List<TagResult> NormalizeTags(List<TagResult> results)
    {
        foreach (var result in results)
        {
            var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in result.Tags)
            {
                var trimmed = tag.Trim();
                if (ValidTags.Contains(trimmed))
                {
                    normalized.Add(ValidTags.First(t => t.Equals(trimmed, StringComparison.OrdinalIgnoreCase)));
                }
                else if (TagAliases.TryGetValue(trimmed, out var mapped))
                {
                    normalized.Add(mapped);
                }
                // else: skip invalid tag
            }
            result.Tags = normalized.ToList();
        }
        return results;
    }

    public static string BuildTaggingInstructions() =>
        """
        You are a job classification expert. You will receive a numbered list of job descriptions (in Polish).
        For each job, assign one or more tags from EXACTLY this list (use these exact strings, no other values):

        1. "IT" — programowanie, administracja systemów, analiza danych, cyberbezpieczeństwo, praca z komputerami
        2. "transport" — kierowcy, logistyka, spedycja, przewozy, transport towarów, kurierzy, zarządzanie łańcuchem dostaw, przepływ towarów, zarządzanie magazynami
        3. "edukacja" — nauczyciele, szkoleniowcy, wykładowcy, tutorzy, wychowawcy, pedagogika
        4. "medycyna" — lekarze, pielęgniarki, farmaceuci, ratownicy medyczni, fizjoterapeuci, mikrobiologia medyczna
        5. "praca z ludźmi" — obsługa klienta, HR, sprzedaż, doradztwo, recepcja, praca socjalna, negocjacje
        6. "praca z pojazdami" — mechanicy, serwisanci pojazdów, operatorzy maszyn, diagnostyka pojazdów
        7. "praca fizyczna" — budownictwo, murarstwo, magazyn, produkcja, sprzątanie, prace porządkowe

        RULES:
        - Use ONLY the 7 tags listed above. Do NOT invent new tags like "logistyka", "nauka", "rolnictwo" etc.
        - If someone works in logistics/spedycja/przepływ towarów → use "transport"
        - If someone works in construction/murarstwo → use "praca fizyczna"
        - One person can have MULTIPLE tags
        - Every job MUST get at least one tag

        After analyzing all jobs, call the SaveTagResults tool with the results as JSON.
        The JSON must have format: {"results": [{"id": 1, "tags": ["tag1", "tag2"]}, ...]}
        """;
}
