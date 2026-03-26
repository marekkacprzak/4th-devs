using System.Text.Json.Serialization;

namespace Negotiations.Models;

public class ToolRequest
{
    [JsonPropertyName("params")]
    public string Params { get; set; } = "";
}
