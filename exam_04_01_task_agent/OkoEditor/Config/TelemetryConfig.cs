namespace OkoEditor.Config;

public class TelemetryConfig
{
    public bool Enabled { get; set; } = true;
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";
    public string ServiceName { get; set; } = "OkoEditor";
    public bool EnableSensitiveData { get; set; } = false;
}
