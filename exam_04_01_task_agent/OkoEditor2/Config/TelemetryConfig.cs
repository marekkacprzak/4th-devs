namespace OkoEditor2.Config;

public class TelemetryConfig
{
    public bool Enabled { get; set; } = true;
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";
    public string ServiceName { get; set; } = "OkoEditor2";
    public bool EnableSensitiveData { get; set; } = false;
}
