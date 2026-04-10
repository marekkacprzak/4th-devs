namespace TimeTravel.Config;

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

public class HubConfig
{
    public string ApiUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string TaskName { get; set; } = "timetravel";
    public string DocsUrl { get; set; } = "https://hub.ag3nts.org/dane/timetravel.md";
    public int MaxRetries { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 1000;
}

public class TelemetryConfig
{
    public bool Enabled { get; set; } = true;
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";
    public string ServiceName { get; set; } = "TimeTravel";
    public bool EnableSensitiveData { get; set; } = false;
}
