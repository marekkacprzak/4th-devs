using System.ComponentModel;
using System.Text.Json;
using WindPower.Services;
using WindPower.UI;

namespace WindPower.Tools;

/// <summary>
/// Provides the single tool exposed to the LLM: CallVerifyApi.
/// Wind power task does not require web page fetching — only /verify API calls.
/// </summary>
public class WindpowerTools
{
    private readonly CentralaApiClient _centrala;

    public WindpowerTools(CentralaApiClient centrala)
    {
        _centrala = centrala;
    }

    [Description("Wywołaj API /verify w centrali z podaną akcją. Dostępne akcje: help, start, weatherForecast, turbineSpecs, powerRequirements, getResult, unlockCodeGenerator, config, turbinecheck, done.")]
    public async Task<string> CallVerifyApi(
        [Description("Nazwa akcji do wywołania (np. help, start, weatherForecast, turbineSpecs, powerRequirements, getResult, unlockCodeGenerator, config, turbinecheck, done)")] string action,
        [Description("Opcjonalne dodatkowe pola w formacie JSON object string. Pomiń lub podaj null jeśli brak dodatkowych pól.")] string? additionalFieldsJson = null)
    {
        ConsoleUI.PrintStep($"CallVerifyApi: action={action}");

        // Treat the string literal "null" the same as actual null
        if (additionalFieldsJson?.Trim() == "null")
            additionalFieldsJson = null;

        object answer;

        if (!string.IsNullOrWhiteSpace(additionalFieldsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(additionalFieldsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    answer = new { action };
                    return await _centrala.VerifyAsync(answer);
                }

                var dict = new Dictionary<string, object?>();
                dict["action"] = action;
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => (object?)prop.Value.GetString(),
                        JsonValueKind.Number when prop.Value.TryGetInt32(out var i) => i,
                        JsonValueKind.Number => prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null,
                        // Preserve nested objects/arrays as JsonElement so they serialize correctly
                        JsonValueKind.Object or JsonValueKind.Array => prop.Value.Clone(),
                        _ => prop.Value.GetRawText()
                    };
                }
                answer = dict;
            }
            catch (JsonException ex)
            {
                ConsoleUI.PrintError($"Failed to parse additionalFieldsJson: {ex.Message}. Calling with action only.");
                answer = new { action };
            }
        }
        else
        {
            answer = new { action };
        }

        return await _centrala.VerifyAsync(answer);
    }
}
