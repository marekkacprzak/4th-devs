using System.Text.Json.Serialization;

namespace FindHimAgent.Models;

public class Suspect
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("surname")]
    public string Surname { get; set; } = "";

    [JsonPropertyName("birthYear")]
    public int BirthYear { get; set; }
}
