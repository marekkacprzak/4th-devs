using System.Text.Json.Serialization;

namespace ProxyAgent.Models;

public class ProxyRequest
{
    [JsonPropertyName("sessionID")]
    public string SessionID { get; set; } = "";

    [JsonPropertyName("msg")]
    public string Msg { get; set; } = "";
}
