namespace DroneAgent.Config;

public class HubConfig
{
    public string ApiUrl { get; set; } = "<HUB_VERIFY_URL>";
    public string DataBaseUrl { get; set; } = "<HUB_DATA_URL>";
    public string ApiKey { get; set; } = "";
    public string TaskName { get; set; } = "drone";
    public int MaxRetries { get; set; } = 5;
    public int RetryDelayMs { get; set; } = 2000;
}
