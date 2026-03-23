using System.Text.Json.Serialization;

namespace EvaluationAgent.Models;

public class SensorData
{
    [JsonPropertyName("sensor_type")]
    public string SensorType { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    [JsonPropertyName("temperature_K")]
    public double TemperatureK { get; set; }

    [JsonPropertyName("pressure_bar")]
    public double PressureBar { get; set; }

    [JsonPropertyName("water_level_meters")]
    public double WaterLevelMeters { get; set; }

    [JsonPropertyName("voltage_supply_v")]
    public double VoltageSupplyV { get; set; }

    [JsonPropertyName("humidity_percent")]
    public double HumidityPercent { get; set; }

    [JsonPropertyName("operator_notes")]
    public string OperatorNotes { get; set; } = "";
}
