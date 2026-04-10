using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using TimeTravel.Config;
using TimeTravel.Services;
using TimeTravel.UI;

namespace TimeTravel.Tools;

public class TimeTravelTools
{
    private readonly HubApiClient _hubApi;
    private readonly HttpClient _httpClient;
    private readonly string _docsUrl;

    private const int MaxDocLength = 12000;

    public TimeTravelTools(HubApiClient hubApi, HttpClient httpClient, HubConfig hubConfig)
    {
        _hubApi = hubApi;
        _httpClient = httpClient;
        _docsUrl = hubConfig.DocsUrl;
    }

    [Description("Call the /verify API for the timetravel task. Actions: help, configure, getConfig, reset. " +
                 "For configure, pass additionalFieldsJson with param and value, e.g. {\"param\":\"year\",\"value\":2238}. " +
                 "Configurable params: day, month, year, syncRatio, stabilization.")]
    public async Task<string> CallVerifyApi(
        [Description("Action to call: help, configure, getConfig, reset")] string action,
        [Description("Optional additional fields as JSON object string, e.g. {\"param\":\"year\",\"value\":2238}. Omit or pass null if not needed.")] string? additionalFieldsJson = null)
    {
        ConsoleUI.PrintStep($"CallVerifyApi: action={action}");

        object answer;

        if (additionalFieldsJson?.Trim() == "null")
            additionalFieldsJson = null;

        if (!string.IsNullOrWhiteSpace(additionalFieldsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(additionalFieldsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    answer = new { action };
                    return await _hubApi.VerifyAsync(answer);
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

        return await _hubApi.VerifyAsync(answer);
    }

    [Description("Fetch the time machine documentation from the server. Call this first to understand device configuration rules, syncRatio formula, PT-A/PT-B switches, PWR protection table, internalMode behavior, and flux density requirements.")]
    public async Task<string> FetchDocumentation()
    {
        ConsoleUI.PrintStep($"FetchDocumentation: {_docsUrl}");
        try
        {
            var response = await _httpClient.GetAsync(_docsUrl);
            var body = await response.Content.ReadAsStringAsync();

            if (body.Length > MaxDocLength)
                body = body[..MaxDocLength] + "\n...[truncated]";

            return body;
        }
        catch (Exception ex)
        {
            return $"Error fetching documentation: {ex.Message}";
        }
    }

    [Description("REQUIRED: Call this tool every time the operator needs to take action in the web UI (set PT-A/PT-B switches, change PWR slider, switch standby/active, or confirm a jump is complete). This PAUSES the agent and waits for operator input. Do NOT skip this tool - without operator confirmation the jumps cannot happen.")]
    public string WaitForOperatorConfirmation(
        [Description("Exact instructions for the operator about what to do in the web UI at https://<hub_url>/timetravel_preview")] string instruction)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║            *** OPERATOR ACTION REQUIRED ***                 ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine(instruction);
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("\nPress ENTER when done (or type your response): ");
        Console.ResetColor();
        var response = Console.ReadLine() ?? "done";
        ConsoleUI.PrintInfo($"Operator responded: {response}");
        return $"Operator confirmed: {response}";
    }

    [Description("Calculate the syncRatio for a given date using the official formula: ((day*8) + (month*12) + (year*7)) mod 101, then divide by 100. Returns a decimal string like '0.82' representing a value between 0.00 and 1.00.")]
    public string CalculateSyncRatio(
        [Description("Day of the month (1-31)")] int day,
        [Description("Month number (1-12)")] int month,
        [Description("Full year, e.g. 2238")] int year)
    {
        int raw = ((day * 8) + (month * 12) + (year * 7)) % 101;
        double ratio = raw / 100.0;
        var result = ratio.ToString("F2", CultureInfo.InvariantCulture);
        ConsoleUI.PrintStep($"CalculateSyncRatio({day},{month},{year}) = raw={raw} → {result}");
        return result;
    }
}
