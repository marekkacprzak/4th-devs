using System.Text.Json.Serialization;

namespace RadioMonitoring.Models;

public class ListenResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("transcription")]
    public string? Transcription { get; set; }

    [JsonPropertyName("meta")]
    public string? Meta { get; set; }

    [JsonPropertyName("attachment")]
    public string? Attachment { get; set; }

    [JsonPropertyName("filesize")]
    public long? Filesize { get; set; }
}
