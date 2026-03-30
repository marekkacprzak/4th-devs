using System.Text.Json.Serialization;

namespace OkoEditor2.Models;

public class VerifyRequest
{
    [JsonPropertyName("apikey")] public string ApiKey { get; set; } = "";
    [JsonPropertyName("task")] public string Task { get; set; } = "okoeditor";
    [JsonPropertyName("answer")] public object Answer { get; set; } = new { };
}
