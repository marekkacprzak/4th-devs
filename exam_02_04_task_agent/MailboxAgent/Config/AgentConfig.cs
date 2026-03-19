namespace MailboxAgent.Config;

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
