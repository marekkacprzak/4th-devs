namespace MailboxAgent.Config;

public class HubConfig
{
    public string ApiUrl { get; set; } = "<HUB_VERIFY_URL>";
    public string ZmailUrl { get; set; } = "https://hub.REDACTED.org/api/zmail";
    public string ApiKey { get; set; } = "";
    public string TaskName { get; set; } = "mailbox";
    public int MaxRetries { get; set; } = 5;
    public int RetryDelayMs { get; set; } = 2000;
}
