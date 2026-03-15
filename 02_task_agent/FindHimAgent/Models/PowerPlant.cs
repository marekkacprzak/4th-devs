using System.Text.Json.Serialization;

namespace FindHimAgent.Models;

public class PowerPlant
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("lat")]
    public double Latitude { get; set; }

    [JsonPropertyName("lon")]
    public double Longitude { get; set; }
}
