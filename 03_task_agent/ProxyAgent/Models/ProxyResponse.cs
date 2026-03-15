using System.Text.Json.Serialization;

namespace ProxyAgent.Models;

public class ProxyResponse
{
    [JsonPropertyName("msg")]
    public string Msg { get; set; } = "";

    public ProxyResponse(string msg) => Msg = msg;
}
