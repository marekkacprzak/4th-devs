namespace DroneAgent.Config;

public class VisionConfig
{
    public string Provider { get; set; } = "lmstudio";
    public string Model { get; set; } = "qwen/qwen3-vl-8b";
    public string Endpoint { get; set; } = "http://localhost:1234/v1";
    public string ApiKey { get; set; } = "";

    public string GetApiKey() =>
        !string.IsNullOrEmpty(ApiKey)
            ? ApiKey
            : Environment.GetEnvironmentVariable("OPENAI_API_KEY")
              ?? "lm-studio";
}
