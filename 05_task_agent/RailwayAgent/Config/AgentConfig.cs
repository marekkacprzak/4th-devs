namespace RailwayAgent.Config;

public class AgentConfig
{
    public string Provider { get; set; } = "openai";
    public string Model { get; set; } = "gpt-5.2";
    public string Endpoint { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = "";

    public string GetApiKey() =>
        !string.IsNullOrEmpty(ApiKey)
            ? ApiKey
            : Environment.GetEnvironmentVariable("OPENAI_API_KEY")
              ?? throw new InvalidOperationException(
                  "OPENAI_API_KEY not set. Provide it in appsettings.json or as environment variable.");
}

public class RailwayConfig
{
    public string ApiUrl { get; set; } = "https://hub.ag3nts.org/verify";
    public string ApiKey { get; set; } = "";
    public string TaskName { get; set; } = "railway";
    public int MaxRetries { get; set; } = 5;
    public int RetryDelayMs { get; set; } = 2000;
}
