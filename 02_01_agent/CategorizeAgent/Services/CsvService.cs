using CategorizeAgent.Models;
using CategorizeAgent.UI;

namespace CategorizeAgent.Services;

public class CsvService
{
    public List<GoodsItem> ParseCsv(string csvContent)
    {
        var items = new List<GoodsItem>();
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length < 2)
            return items;

        var separator = lines[0].Contains(';') ? ';' : ',';
        var headers = ParseCsvLine(lines[0], separator)
            .Select(h => h.Trim().ToLowerInvariant())
            .ToList();

        var idIdx = headers.FindIndex(h => h is "id" or "identifier" or "identyfikator" or "code");
        var descIdx = headers.FindIndex(h => h is "description" or "opis" or "desc" or "nazwa");

        if (idIdx < 0 || descIdx < 0)
        {
            ConsoleUI.PrintError($"CSV header not recognized. Headers: {string.Join(", ", headers)}");
            // Fallback: assume first column is id, second is description
            idIdx = 0;
            descIdx = 1;
        }

        for (int i = 1; i < lines.Length; i++)
        {
            var fields = ParseCsvLine(lines[i], separator);
            if (fields.Count <= Math.Max(idIdx, descIdx))
                continue;

            items.Add(new GoodsItem
            {
                Id = fields[idIdx].Trim(),
                Description = fields[descIdx].Trim()
            });
        }

        return items;
    }

    private List<string> ParseCsvLine(string line, char separator)
    {
        var fields = new List<string>();
        var current = "";
        var inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current += '"';
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current += c;
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
                    fields.Add(current);
                    current = "";
                }
                else
                {
                    current += c;
                }
            }
        }

        fields.Add(current);
        return fields;
    }
}
