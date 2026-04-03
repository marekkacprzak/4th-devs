using System.Text.Json.Serialization;

namespace FoodWareHouse.Models;

public class VerifyRequest
{
    [JsonPropertyName("apikey")] public string ApiKey { get; set; } = "";
    [JsonPropertyName("task")] public string Task { get; set; } = "foodwarehouse";
    [JsonPropertyName("answer")] public object Answer { get; set; } = new { };
}
