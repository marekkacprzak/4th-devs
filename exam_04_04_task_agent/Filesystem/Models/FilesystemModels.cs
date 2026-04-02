using System.Text.Json.Serialization;

namespace Filesystem.Models;

/// <summary>A city participating in trade. Holds goods it needs with quantities.</summary>
public class TradeCity
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("needs")]
    public Dictionary<string, int> Needs { get; set; } = new();
}

/// <summary>A person managing trade for a specific city.</summary>
public class TradePerson
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = "";

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = "";

    [JsonPropertyName("city_file")]
    public string CityFileName { get; set; } = "";

    [JsonPropertyName("city_display")]
    public string CityDisplayName { get; set; } = "";
}

/// <summary>A good offered for sale by a specific city.</summary>
public class TradeGood
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("city_file")]
    public string CityFileName { get; set; } = "";

    [JsonPropertyName("city_display")]
    public string CityDisplayName { get; set; } = "";
}

/// <summary>Complete parsed trade data from Natan's notes.</summary>
public class TradeData
{
    [JsonPropertyName("cities")]
    public List<TradeCity> Cities { get; set; } = new();

    [JsonPropertyName("people")]
    public List<TradePerson> People { get; set; } = new();

    [JsonPropertyName("goods")]
    public List<TradeGood> Goods { get; set; } = new();

    /// <summary>Populated by C# parser from transakcje.txt: good_name -> list of (sellerFileName, sellerDisplayName).</summary>
    [JsonIgnore]
    public Dictionary<string, List<(string FileName, string DisplayName)>> GoodsMap { get; set; } = new();
}
