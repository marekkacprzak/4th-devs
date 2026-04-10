using System.Text.Json.Serialization;

namespace TimeTravel.Models;

public class VerifyRequest
{
    [JsonPropertyName("apikey")] public string ApiKey { get; set; } = "";
    [JsonPropertyName("task")] public string Task { get; set; } = "timetravel";
    [JsonPropertyName("answer")] public object Answer { get; set; } = new { };
}
