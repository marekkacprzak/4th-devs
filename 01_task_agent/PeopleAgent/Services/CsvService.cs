using PeopleAgent.Models;
using PeopleAgent.UI;

namespace PeopleAgent.Services;

public class CsvService
{
    private readonly HttpClient _http;

    public CsvService(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> DownloadCsvAsync(string url)
    {
        ConsoleUI.PrintInfo($"Downloading CSV from {url}");
        var content = await _http.GetStringAsync(url);
        ConsoleUI.PrintInfo($"Downloaded {content.Length} chars");
        return content;
    }

    public List<Person> ParseCsv(string csvContent)
    {
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
            return new List<Person>();

        var header = lines[0].Trim();
        var separator = header.Contains(';') ? ';' : ',';
        var headerFields = ParseCsvLine(header, separator);
        var columns = headerFields.Select(c => c.Trim().ToLowerInvariant()).ToArray();

        var nameIdx = Array.FindIndex(columns, c => c is "name" or "imie" or "imię");
        var surnameIdx = Array.FindIndex(columns, c => c is "surname" or "nazwisko");
        var genderIdx = Array.FindIndex(columns, c => c is "gender" or "plec" or "płeć");
        var bornIdx = Array.FindIndex(columns, c => c is "born" or "rok_urodzenia" or "urodzony" or "birthdate");
        var cityIdx = Array.FindIndex(columns, c => c is "city" or "miasto" or "birthplace");
        var jobIdx = Array.FindIndex(columns, c => c is "job" or "stanowisko" or "praca" or "zawod" or "zawód");

        ConsoleUI.PrintInfo($"CSV columns: [{string.Join(", ", columns)}]");
        ConsoleUI.PrintInfo($"Mapped indices: name={nameIdx}, surname={surnameIdx}, gender={genderIdx}, born={bornIdx}, city={cityIdx}, job={jobIdx}");

        var people = new List<Person>();

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var fields = ParseCsvLine(line, separator);

            var person = new Person();

            if (nameIdx >= 0 && nameIdx < fields.Length)
                person.Name = fields[nameIdx].Trim();
            if (surnameIdx >= 0 && surnameIdx < fields.Length)
                person.Surname = fields[surnameIdx].Trim();
            if (genderIdx >= 0 && genderIdx < fields.Length)
                person.Gender = fields[genderIdx].Trim();
            if (bornIdx >= 0 && bornIdx < fields.Length)
            {
                var bornStr = fields[bornIdx].Trim();
                if (DateTime.TryParse(bornStr, out var birthDate))
                    person.Born = birthDate.Year;
                else if (int.TryParse(bornStr, out var bornYear))
                    person.Born = bornYear;
            }
            if (cityIdx >= 0 && cityIdx < fields.Length)
                person.City = fields[cityIdx].Trim();
            if (jobIdx >= 0 && jobIdx < fields.Length)
                person.Job = fields[jobIdx].Trim();

            people.Add(person);
        }

        ConsoleUI.PrintInfo($"Parsed {people.Count} people from CSV");
        return people;
    }

    /// <summary>
    /// Parse a CSV line handling quoted fields (fields may contain separator and newlines).
    /// </summary>
    private static string[] ParseCsvLine(string line, char separator)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // Check for escaped quote ""
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++; // skip next quote
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == separator)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
