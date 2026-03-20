using System.ComponentModel;
using System.Diagnostics;
using DroneAgent.Services;
using DroneAgent.UI;

namespace DroneAgent.Tools;

public class DroneCommandTool
{
    private static readonly ActivitySource Activity = new("DroneAgent.Tools");

    private readonly HubApiClient _hubApi;

    public DroneCommandTool(HubApiClient hubApi)
    {
        _hubApi = hubApi;
    }

    [Description("Send drone instructions to the hub API. Pass instructions separated by pipe '|' character. Returns API response with error feedback or {FLG:...} on success. Example: 'hardReset|setDestinationObject(PWR6132PL)|set(5,3)|set(engineON)|set(100)|set(50m)|set(destroy)|flyToLocation'")]
    public async Task<string> SendInstructions(
        [Description("Pipe-separated drone instructions, e.g. 'hardReset|set(3,4)|flyToLocation'")] string instructionsPiped)
    {
        using var span = Activity.StartActivity("tool.SendInstructions");

        var instructions = instructionsPiped
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        span?.SetTag("tool.name", "SendInstructions");
        span?.SetTag("tool.instructions", string.Join(" | ", instructions));
        span?.SetTag("tool.instruction_count", instructions.Length);

        ConsoleUI.PrintToolCall("SendInstructions", string.Join(", ", instructions));

        var response = await _hubApi.SubmitInstructionsAsync(instructions);

        span?.SetTag("tool.response", response);

        return response;
    }
}
