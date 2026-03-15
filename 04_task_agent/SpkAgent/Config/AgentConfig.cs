namespace SpkAgent.Config;

public class AgentConfig
{
    public string Provider { get; set; } = "lmstudio";
    public string Model { get; set; } = "qwen3-coder-30b-a3b-instruct-mlx";
    public string Endpoint { get; set; } = "http://localhost:1234/v1";
    public string ApiKey { get; set; } = "";

    public string GetApiKey() =>
        !string.IsNullOrEmpty(ApiKey)
            ? ApiKey
            : Environment.GetEnvironmentVariable("OPENAI_API_KEY")
              ?? "lm-studio";
}

public class VisionConfig
{
    public string Provider { get; set; } = "lmstudio";
    public string Model { get; set; } = "qwen3_vl";
    public string Endpoint { get; set; } = "http://localhost:1235/v1";
}

public class HubConfig
{
    public string ApiUrl { get; set; } = "<HUB_VERIFY_URL>";
    public string DocsBaseUrl { get; set; } = "https://hub.REDACTED.org/dane/doc";
    public string ApiKey { get; set; } = "";
    public string TaskName { get; set; } = "sendit";
    public int MaxRetries { get; set; } = 5;
    public int RetryDelayMs { get; set; } = 2000;
}
