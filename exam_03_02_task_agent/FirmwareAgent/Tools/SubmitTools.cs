using System.ComponentModel;
using FirmwareAgent.Services;
using FirmwareAgent.UI;

namespace FirmwareAgent.Tools;

public class SubmitTools
{
    private readonly HubApiClient _api;

    public SubmitTools(HubApiClient api)
    {
        _api = api;
    }

    [Description(
        "Submit the ECCS confirmation code to the hub for verification. " +
        "The code format is: ECCS-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx " +
        "Only call this tool once you have found the actual confirmation code displayed by the firmware. " +
        "Do NOT guess or make up a code — use exactly what the firmware printed.")]
    public async Task<string> SubmitAnswer(
        [Description("The ECCS confirmation code exactly as displayed by the firmware, e.g. 'ECCS-abc123def456...'")] string confirmation)
    {
        ConsoleUI.PrintToolCall("SubmitAnswer", $"confirmation={confirmation}");
        var result = await _api.SubmitAnswerAsync(confirmation);
        ConsoleUI.PrintResult($"Submission result: {result}");
        return result;
    }
}
