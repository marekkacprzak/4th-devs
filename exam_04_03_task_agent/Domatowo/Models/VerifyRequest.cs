using System.Text.Json.Serialization;

namespace Domatowo.Models;

public class VerifyRequest
{
    [JsonPropertyName("apikey")] public string ApiKey { get; set; } = "";
    [JsonPropertyName("task")] public string Task { get; set; } = "domatowo";
    [JsonPropertyName("answer")] public object Answer { get; set; } = new { };
}
