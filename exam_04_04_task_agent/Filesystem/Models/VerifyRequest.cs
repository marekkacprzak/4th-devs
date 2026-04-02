using System.Text.Json.Serialization;

namespace Filesystem.Models;

public class VerifyRequest
{
    [JsonPropertyName("apikey")] public string ApiKey { get; set; } = "";
    [JsonPropertyName("task")] public string Task { get; set; } = "filesystem";
    [JsonPropertyName("answer")] public object Answer { get; set; } = new { };
}
