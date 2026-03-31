using System.Text.Json.Serialization;

namespace WindPower.Models;

public class VerifyRequest
{
    [JsonPropertyName("apikey")] public string ApiKey { get; set; } = "";
    [JsonPropertyName("task")] public string Task { get; set; } = "windpower";
    [JsonPropertyName("answer")] public object Answer { get; set; } = new { };
}
