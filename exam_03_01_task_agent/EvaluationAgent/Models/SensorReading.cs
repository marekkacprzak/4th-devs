namespace EvaluationAgent.Models;

public class SensorReading
{
    public string FileId { get; set; } = "";
    public SensorData Data { get; set; } = new();
    public bool DataIsValid { get; set; }
    public AnomalyType Anomalies { get; set; } = AnomalyType.None;
}
