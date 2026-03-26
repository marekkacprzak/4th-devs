using System.Text.Json.Serialization;

namespace Negotiations.Models;

public class ToolResponse
{
    [JsonPropertyName("output")]
    public string Output { get; set; }

    public ToolResponse(string output) => Output = output;
}
