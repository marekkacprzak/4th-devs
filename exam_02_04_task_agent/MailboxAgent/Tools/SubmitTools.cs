using System.ComponentModel;
using MailboxAgent.Services;
using MailboxAgent.UI;

namespace MailboxAgent.Tools;

public class SubmitTools
{
    private readonly HubApiClient _api;

    public SubmitTools(HubApiClient api)
    {
        _api = api;
    }

    [Description("Submit the extracted answer to the verification hub. Call this once you have found all three values: password, attack date, and confirmation code. Returns feedback indicating which values are correct/incorrect, or a flag {FLG:...} on success.")]
    public async Task<string> SubmitAnswer(
        [Description("The employee system password found in the emails")] string password,
        [Description("The planned attack date in YYYY-MM-DD format")] string date,
        [Description("The security confirmation code in format SEC- followed by 28 characters (32 total)")] string confirmationCode)
    {
        ConsoleUI.PrintToolCall("SubmitAnswer", $"password={password}, date={date}, code={confirmationCode}");

        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(date) || string.IsNullOrWhiteSpace(confirmationCode))
            return "ERROR: All three values (password, date, confirmationCode) must be provided. Keep searching if any are missing.";

        return await _api.SubmitAnswerAsync(password, date, confirmationCode);
    }

    [Description("Call this when the task is complete (flag received) or you have exhausted all search options. Returns a completion signal.")]
    public string FinishWork(
        [Description("Summary of what was accomplished or reason for stopping")] string summary)
    {
        ConsoleUI.PrintToolCall("FinishWork", summary);
        ConsoleUI.PrintResult($"Agent finished: {summary}");
        return $"FINISH: {summary}";
    }
}
