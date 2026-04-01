using System.ComponentModel;
using System.Text.Json;
using Domatowo.Services;
using Domatowo.UI;

namespace Domatowo.Tools;

/// <summary>
/// Exposes the Domatowo API actions as callable methods.
/// Field names match the actual API: "object"/"where" for move, "object" for inspect/dismount.
/// </summary>
public class DomatowoTools
{
    private readonly CentralaApiClient _centrala;

    public DomatowoTools(CentralaApiClient centrala)
    {
        _centrala = centrala;
    }

    public Task<string> Help()
        => _centrala.VerifyAsync(new { action = "help" });

    public Task<string> Reset()
        => _centrala.VerifyAsync(new { action = "reset" });

    public Task<string> ActionCost()
        => _centrala.VerifyAsync(new { action = "actionCost" });

    public Task<string> GetMap(string[]? symbols = null)
    {
        object answer = symbols is { Length: > 0 }
            ? new { action = "getMap", symbols }
            : new { action = "getMap" };
        return _centrala.VerifyAsync(answer);
    }

    public Task<string> SearchSymbol(string symbol)
        => _centrala.VerifyAsync(new { action = "searchSymbol", symbol });

    public Task<string> GetObjects()
        => _centrala.VerifyAsync(new { action = "getObjects" });

    public Task<string> GetLogs()
        => _centrala.VerifyAsync(new { action = "getLogs" });

    public Task<string> Expenses()
        => _centrala.VerifyAsync(new { action = "expenses" });

    /// <summary>
    /// Creates a transporter with N scout passengers. Cost: 5 + 5*passengers pts.
    /// Response: { "object": "T_HASH", "crew": [{"id": "S_HASH", "role": "scout"}, ...] }
    /// </summary>
    public Task<string> CreateTransporter(int passengers)
        => _centrala.VerifyAsync(new { action = "create", type = "transporter", passengers });

    /// <summary>
    /// Creates a standalone scout. Cost: 5 pts.
    /// Response: { "object": "UNIT_HASH", "crew": [{"id": "SCOUT_HASH", "role": "scout"}] }
    /// </summary>
    public Task<string> CreateScout()
        => _centrala.VerifyAsync(new { action = "create", type = "scout" });

    /// <summary>
    /// Moves a unit (transporter or scout) to the destination coord (e.g. "F2").
    /// API field name: "object" for unit hash, "where" for destination.
    /// Cost: 1pt/field for transporter (road only), 7pt/field for scout (any terrain).
    /// </summary>
    public Task<string> MoveUnit(string unitHash, string where)
        => _centrala.VerifyAsync(new { action = "move", @object = unitHash, where });

    /// <summary>
    /// Dismounts N scouts from a transporter, spawning them on adjacent free tiles.
    /// Cost: 0 pts.
    /// </summary>
    public Task<string> Dismount(string transporterHash, int passengers)
        => _centrala.VerifyAsync(new { action = "dismount", @object = transporterHash, passengers });

    /// <summary>
    /// Inspects the scout's current field, appending a log entry.
    /// Cost: 1 pt.
    /// </summary>
    public Task<string> InspectField(string scoutHash)
        => _centrala.VerifyAsync(new { action = "inspect", @object = scoutHash });

    /// <summary>
    /// Calls the rescue helicopter to the confirmed survivor location.
    /// Only works after a scout has confirmed a human via inspect.
    /// </summary>
    public Task<string> CallHelicopter(string destination)
        => _centrala.VerifyAsync(new { action = "callHelicopter", destination });

    [Description("Wywołaj dowolną akcję API z opcjonalnymi polami JSON.")]
    public async Task<string> CallVerifyApi(
        [Description("Nazwa akcji")] string action,
        [Description("Opcjonalne dodatkowe pola jako JSON object string")] string? additionalFieldsJson = null)
    {
        ConsoleUI.PrintStep($"CallVerifyApi: action={action}");

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
