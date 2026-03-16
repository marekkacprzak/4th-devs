namespace RailwayAgent.Config;

public class TelemetryConfig
{
    public bool Enabled { get; set; } = true;
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";
    public string ServiceName { get; set; } = "RailwayAgent";
    public bool EnableSensitiveData { get; set; }
}
