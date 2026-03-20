namespace DroneAgent.Config;

public class HubConfig
{
    public string ApiUrl { get; set; } = "";
    public string DataBaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string TaskName { get; set; } = "drone";
    public int MaxRetries { get; set; } = 5;
    public int RetryDelayMs { get; set; } = 2000;
}
