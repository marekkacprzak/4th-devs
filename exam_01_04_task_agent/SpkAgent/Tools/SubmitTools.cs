using System.ComponentModel;
using SpkAgent.Services;
using SpkAgent.UI;

namespace SpkAgent.Tools;

public class SubmitTools
{
    private readonly HubApiClient _api;

    public SubmitTools(HubApiClient api)
    {
        _api = api;
    }

    [Description("Submit a completed declaration to the Hub verification endpoint. Returns the server response. Look for {FLG:...} in successful responses.")]
    public async Task<string> SubmitDeclaration(
        [Description("The complete declaration text to submit")] string declaration)
    {
        ConsoleUI.PrintToolCall("SubmitDeclaration", $"declaration_length={declaration.Length}");
        return await _api.SubmitDeclarationAsync(declaration);
    }
}
