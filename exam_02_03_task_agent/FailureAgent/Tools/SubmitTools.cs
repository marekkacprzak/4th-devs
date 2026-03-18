using System.ComponentModel;
using FailureAgent.Services;
using FailureAgent.UI;

namespace FailureAgent.Tools;

public class SubmitTools
{
    private readonly HubApiClient _api;
    private readonly LogSearchTools _logTools;

    public SubmitTools(HubApiClient api, LogSearchTools logTools)
    {
        _api = api;
        _logTools = logTools;
    }

    [Description("Submit current condensed log draft to Centrala for verification. Returns technician feedback or a flag {FLG:...} on success.")]
    public async Task<string> SubmitLogs()
    {
        var draft = _logTools.GetDraftText();
        var tokenCount = _logTools.GetTokenCount();

        ConsoleUI.PrintToolCall("SubmitLogs", $"tokens=~{tokenCount}, lines={_logTools.GetDraftText().Count(c => c == '\n') + 1}");

        if (string.IsNullOrWhiteSpace(draft))
            return "ERROR: Draft is empty. Add log entries first using AddLogEntry or AddMultipleEntries.";

        if (tokenCount > 1500)
            return $"ERROR: Draft has ~{tokenCount} estimated tokens, exceeding 1500 limit. Remove or shorten entries first.";

        return await _api.SubmitLogsAsync(draft);
    }
}
