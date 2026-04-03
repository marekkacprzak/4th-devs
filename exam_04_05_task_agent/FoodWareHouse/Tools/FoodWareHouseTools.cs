using FoodWareHouse.Services;

namespace FoodWareHouse.Tools;

/// <summary>
/// API wrappers for the foodwarehouse task.
/// All calls use the "tool" field pattern: { tool: "...", [action: "..."], [other fields] }
/// </summary>
public class FoodWareHouseTools
{
    private readonly CentralaApiClient _centrala;
    private readonly HttpClient _http;

    public FoodWareHouseTools(CentralaApiClient centrala, HttpClient http)
    {
        _centrala = centrala;
        _http = http;
    }

    /// <summary>Get API documentation</summary>
    public Task<string> Help() =>
        _centrala.VerifyAsync(new { tool = "help" });

    /// <summary>Reset task to initial state</summary>
    public Task<string> Reset() =>
        _centrala.VerifyAsync(new { tool = "reset" });

    /// <summary>Final verification — returns flag if all orders are correct</summary>
    public Task<string> Done() =>
        _centrala.VerifyAsync(new { tool = "done" });

    /// <summary>Execute a read-only SQLite query</summary>
    public Task<string> DatabaseQuery(string query) =>
        _centrala.VerifyAsync(new { tool = "database", query });

    /// <summary>List all current orders</summary>
    public Task<string> OrdersGet() =>
        _centrala.VerifyAsync(new { tool = "orders", action = "get" });

    /// <summary>Create a new order for a city</summary>
    public Task<string> OrdersCreate(string title, int creatorID, int destination, string signature) =>
        _centrala.VerifyAsync(new { tool = "orders", action = "create", title, creatorID, destination, signature });

    /// <summary>Append items to an existing order (batch mode)</summary>
    public Task<string> OrdersAppend(string orderId, Dictionary<string, int> items) =>
        _centrala.VerifyAsync(new { tool = "orders", action = "append", id = orderId, items });

    /// <summary>
    /// Generate SHA1 signature.
    /// Requires: login (user login), birthday (YYYY-MM-DD), destination (numeric city code).
    /// </summary>
    public Task<string> GenerateSignature(string login, string birthday, int destination) =>
        _centrala.VerifyAsync(new { tool = "signatureGenerator", action = "generate", login, birthday, destination });

    /// <summary>Fetch the food4cities.json demand file</summary>
    public Task<string> FetchCityDemands(string url) =>
        _centrala.GetStringAsync(url);
}
