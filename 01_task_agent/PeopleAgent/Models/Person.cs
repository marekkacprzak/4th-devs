using System.Text.Json.Serialization;

namespace PeopleAgent.Models;

public class Person
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("surname")]
    public string Surname { get; set; } = "";

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = "";

    [JsonPropertyName("born")]
    public int Born { get; set; }

    [JsonPropertyName("city")]
    public string City { get; set; } = "";

    [JsonIgnore]
    public string Job { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
}
