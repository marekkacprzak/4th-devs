using System.Text.Json.Serialization;

namespace FoodWareHouse.Models;

/// <summary>Demand for a single city from food4cities.json</summary>
public class CityDemand
{
    public string City { get; set; } = "";
    public Dictionary<string, int> Items { get; set; } = new();
}

/// <summary>Tracks an order created for a city during Phase 4</summary>
public class CreatedOrder
{
    public string OrderId { get; set; } = "";
    public string City { get; set; } = "";
}

/// <summary>Row from the DB users/creators table (fields discovered dynamically)</summary>
public class DbUser
{
    public int Id { get; set; }
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    // Additional fields populated by parsing raw DB results
    public Dictionary<string, string> ExtraFields { get; set; } = new();
}

/// <summary>Destination mapping: city name → destination code</summary>
public class DbDestination
{
    public string City { get; set; } = "";
    public string Code { get; set; } = "";
}
