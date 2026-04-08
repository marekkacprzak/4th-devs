using System.Text.Json.Serialization;

namespace Phonecall.Models;

public class VerifyRequest
{
    [JsonPropertyName("apikey")] public string ApiKey { get; set; } = "";
    [JsonPropertyName("task")] public string Task { get; set; } = "phonecall";
    [JsonPropertyName("answer")] public object Answer { get; set; } = new { };
}
