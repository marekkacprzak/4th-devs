using System.ComponentModel;
using MailboxAgent.Services;
using MailboxAgent.UI;

namespace MailboxAgent.Tools;

public class ZmailTools
{
    private readonly ZmailApiClient _api;

    public ZmailTools(ZmailApiClient api)
    {
        _api = api;
    }

    [Description("Discover available API actions and parameters for the zmail inbox API. Call this first to understand what actions and parameters are available.")]
    public async Task<string> CallHelp()
    {
        ConsoleUI.PrintToolCall("CallHelp");
        return await _api.CallHelpAsync();
    }

    [Description("List threads in the inbox (no search filtering). Returns thread metadata without message body. Use Search instead if you need to find specific emails.")]
    public async Task<string> GetInbox(
        [Description("Page number, starting from 1")] int page = 1,
        [Description("Results per page, between 5 and 20")] int perPage = 20)
    {
        ConsoleUI.PrintToolCall("GetInbox", $"page={page}, perPage={perPage}");
        return await _api.GetInboxAsync(page, perPage);
    }

    [Description("Search messages with Gmail-like query operators. Returns matching message metadata. Supports: words, \"phrase\", -exclude, from:, to:, subject:, subject:\"phrase\", OR, AND. Missing operator means AND. Examples: 'from:proton.me', 'subject:password', 'from:vik4tor@proton.me OR subject:SEC-'.")]
    public async Task<string> Search(
        [Description("Search query with Gmail-like operators. Required.")] string query,
        [Description("Page number, starting from 1")] int page = 1,
        [Description("Results per page, between 5 and 20")] int perPage = 20)
    {
        ConsoleUI.PrintToolCall("Search", $"query={query}, page={page}, perPage={perPage}");
        return await _api.SearchAsync(query, page, perPage);
    }

    [Description("Get the list of rowIDs and messageIDs for a specific thread. No message body is returned. Use GetMessages afterwards to read full message content.")]
    public async Task<string> GetThread(
        [Description("Thread ID (numeric) from inbox or search results")] int threadId)
    {
        ConsoleUI.PrintToolCall("GetThread", $"threadID={threadId}");
        return await _api.GetThreadAsync(threadId);
    }

    [Description("Get full message content by rowID or 32-char messageID. Returns complete message body. Pass ONE ID at a time for best results. Example: GetMessages(id=\"94\") or GetMessages(id=\"c47e6eb8a0e7295356f7d95fe16f01f3\").")]
    public async Task<string> GetMessages(
        [Description("A single numeric rowID or 32-char messageID. Pass only ONE ID.")] string id)
    {
        ConsoleUI.PrintToolCall("GetMessages", $"id={id}");

        // Parse: if it looks numeric, send as int; otherwise send as string
        if (int.TryParse(id.Trim(), out int rowId))
            return await _api.GetMessagesAsync(rowId);

        return await _api.GetMessagesAsync(id.Trim());
    }

    [Description("Reset the API request counter in case of rate limiting issues. Use this if you keep getting rate limit errors.")]
    public async Task<string> ResetRateLimit()
    {
        ConsoleUI.PrintToolCall("ResetRateLimit");
        return await _api.ResetAsync();
    }
}
