using System.Text.Json;
using FoodWareHouse.Models;
using FoodWareHouse.Tools;
using FoodWareHouse.UI;
using Microsoft.Extensions.AI;

namespace FoodWareHouse.Services;

/// <summary>
/// Phased orchestrator for the foodwarehouse task.
///
/// Phase 0: Discovery and Reset   — get API help, reset state, verify initial orders
/// Phase 1: Fetch City Demands    — download food4cities.json, parse city/item requirements
/// Phase 2: Database Exploration  — discover table schema, map cities→destinations, find creator data
/// Phase 3: Generate Signatures   — call signatureGenerator(login, birthday, destination) per city
/// Phase 4: Create Orders         — create one order per city and append items (batch mode)
/// Phase 5: Verify and Submit     — list orders, call done, return flag
/// </summary>
public class FoodWareHouseOrchestrator
{
    private readonly FoodWareHouseTools _tools;
    private readonly IChatClient _chatClient;
    private readonly RunLogger _logger;
    private readonly string _food4CitiesUrl;

    public FoodWareHouseOrchestrator(
        FoodWareHouseTools tools,
        IChatClient chatClient,
        RunLogger logger,
        string food4CitiesUrl)
    {
        _tools = tools;
        _chatClient = chatClient;
        _logger = logger;
        _food4CitiesUrl = food4CitiesUrl;
    }

    public async Task<string> RunAsync()
    {
        // ══════════════════════════════════════════════════════════════
        // Phase 0: Discovery and Reset
        // ══════════════════════════════════════════════════════════════
        ConsoleUI.PrintPhase(0, "Discovery and Reset");
        _logger.LogPhase(0, "Discovery and Reset");

        var helpResponse = await _tools.Help();
        _logger.LogInfo($"Help response:\n{helpResponse}");
        ConsoleUI.PrintInfo("API help fetched.");

        var resetResponse = await _tools.Reset();
        _logger.LogInfo($"Reset response: {resetResponse}");
        ConsoleUI.PrintSuccess("Task state reset.");

        var initialOrders = await _tools.OrdersGet();
        _logger.LogInfo($"Initial orders state: {initialOrders}");
        ConsoleUI.PrintInfo("Initial orders state logged.");

        // ══════════════════════════════════════════════════════════════
        // Phase 1: Fetch City Demands
        // ══════════════════════════════════════════════════════════════
        ConsoleUI.PrintPhase(1, "Fetch City Demands");
        _logger.LogPhase(1, "Fetch City Demands");

        var demandsJson = await _tools.FetchCityDemands(_food4CitiesUrl);
        _logger.LogInfo($"food4cities.json raw:\n{demandsJson}");

        var cityDemands = ParseCityDemands(demandsJson);
        _logger.LogInfo($"Parsed {cityDemands.Count} city demands: {string.Join(", ", cityDemands.Select(c => c.City))}");
        ConsoleUI.PrintSuccess($"Fetched demands for {cityDemands.Count} cities: {string.Join(", ", cityDemands.Select(c => c.City))}");

        // ══════════════════════════════════════════════════════════════
        // Phase 2: Database Exploration
        // ══════════════════════════════════════════════════════════════
        ConsoleUI.PrintPhase(2, "Database Exploration");
        _logger.LogPhase(2, "Database Exploration");

        // Discover tables
        var tablesResponse = await _tools.DatabaseQuery("show tables");
        _logger.LogDbQuery("show tables", tablesResponse);
        var tableNames = ParseShowTablesResponse(tablesResponse);
        _logger.LogInfo($"Discovered tables: {string.Join(", ", tableNames)}");
        ConsoleUI.PrintInfo($"Tables: {string.Join(", ", tableNames)}");

        // Read all tables
        var tableData = new Dictionary<string, string>();
        foreach (var table in tableNames)
        {
            var data = await _tools.DatabaseQuery($"select * from {table}");
            _logger.LogDbQuery($"select * from {table}", data);
            tableData[table] = data;
            ConsoleUI.PrintInfo($"Table '{table}' queried, length={data.Length}");
        }

        // Extract destination codes (city name → numeric code)
        var destinations = ExtractDestinations(tableData);
        _logger.LogInfo($"Destination codes:\n{JsonSerializer.Serialize(destinations, new JsonSerializerOptions { WriteIndented = true })}");
        ConsoleUI.PrintSuccess($"Found {destinations.Count} destination codes.");

        // Extract transport role ID from roles table
        var transportRoleId = ExtractTransportRoleId(tableData);
        _logger.LogInfo($"Transport role ID: {transportRoleId}");

        // Extract creator info (first active user with transport role)
        var creatorInfo = ExtractCreatorInfo(tableData, transportRoleId);
        _logger.LogInfo($"Creator info: id={creatorInfo.Id}, login={creatorInfo.Login}, birthday={creatorInfo.Birthday}, role={creatorInfo.RoleId}");
        ConsoleUI.PrintSuccess($"Found creator: id={creatorInfo.Id}, login={creatorInfo.Login}, birthday={creatorInfo.Birthday}, role={creatorInfo.RoleId}");

        // For any cities missing from bulk query, run targeted queries
        var missingCities = cityDemands.Select(c => c.City)
            .Where(c => !destinations.Keys.Any(k => string.Equals(k, c, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var missingCity in missingCities)
        {
            // Capitalize first letter for SQL LIKE query
            var cityCapitalized = char.ToUpper(missingCity[0]) + missingCity[1..].ToLower();
            var query = $"select * from destinations where name = '{cityCapitalized}'";
            var response = await _tools.DatabaseQuery(query);
            _logger.LogDbQuery(query, response);

            var partial = new Dictionary<string, int>();
            partial = ExtractDestinationsFromResponse(response, partial);
            foreach (var (k, v) in partial)
                destinations[k] = v;

            _logger.LogInfo($"Targeted query for '{missingCity}': found {partial.Count} result(s)");
        }

        // Re-check after targeted queries
        var stillMissing = cityDemands.Select(c => c.City)
            .Where(c => !destinations.Keys.Any(k => string.Equals(k, c, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (stillMissing.Count > 0)
        {
            _logger.LogError("Phase2", $"Available keys: {string.Join(", ", destinations.Keys)}\nStill missing: {string.Join(", ", stillMissing)}");
            ConsoleUI.PrintError($"Missing destinations for: {string.Join(", ", stillMissing)}");
            return $"ERROR: Missing destination codes for cities: {string.Join(", ", stillMissing)}";
        }

        if (creatorInfo.Id == 0 || string.IsNullOrWhiteSpace(creatorInfo.Login) || string.IsNullOrWhiteSpace(creatorInfo.Birthday))
        {
            return $"ERROR: Could not find creator info. Got: id={creatorInfo.Id}, login={creatorInfo.Login}, birthday={creatorInfo.Birthday}";
        }

        // ══════════════════════════════════════════════════════════════
        // Phase 3: Generate Signatures (per city — destination differs)
        // ══════════════════════════════════════════════════════════════
        ConsoleUI.PrintPhase(3, "Generate Signatures");
        _logger.LogPhase(3, "Generate Signatures");

        var citySignatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var demand in cityDemands)
        {
            var destCode = GetDestination(destinations, demand.City);
            var sigResponse = await _tools.GenerateSignature(creatorInfo.Login, creatorInfo.Birthday, destCode);
            _logger.LogInfo($"Signature response for {demand.City} (dest={destCode}): {sigResponse}");

            var signature = ExtractSignatureValue(sigResponse);
            if (string.IsNullOrWhiteSpace(signature))
            {
                _logger.LogError("Phase3", $"Could not extract signature for {demand.City}: {sigResponse}");
                return $"ERROR: Could not extract signature for {demand.City}: {sigResponse}";
            }

            citySignatures[demand.City] = signature;
            _logger.LogSignature(demand.City, signature);
            ConsoleUI.PrintSuccess($"Signature for {demand.City}: {signature}");
        }

        // ══════════════════════════════════════════════════════════════
        // Phase 4: Create Orders and Append Items
        // ══════════════════════════════════════════════════════════════
        ConsoleUI.PrintPhase(4, "Create Orders and Append Items");
        _logger.LogPhase(4, "Create Orders and Append Items");

        var createdOrders = new List<CreatedOrder>();
        foreach (var demand in cityDemands)
        {
            var destCode = GetDestination(destinations, demand.City);
            var signature = citySignatures[demand.City];
            var title = $"Dostawa dla {demand.City}";

            ConsoleUI.PrintInfo($"Creating order for {demand.City} (dest={destCode})...");

            var createResponse = await _tools.OrdersCreate(title, creatorInfo.Id, destCode, signature);
            _logger.LogInfo($"OrdersCreate for {demand.City}: {createResponse}");

            var orderId = ExtractOrderId(createResponse);
            if (string.IsNullOrWhiteSpace(orderId))
            {
                _logger.LogError("Phase4", $"Could not extract order ID for {demand.City}: {createResponse}");
                return $"ERROR: Could not extract order ID for {demand.City}: {createResponse}";
            }

            _logger.LogOrderCreated(demand.City, orderId);
            ConsoleUI.PrintSuccess($"Order {orderId} created for {demand.City}.");

            var itemsJson = JsonSerializer.Serialize(demand.Items);
            var appendResponse = await _tools.OrdersAppend(orderId, demand.Items);
            _logger.LogOrderItems(orderId, $"items={itemsJson}\nresponse={appendResponse}");
            ConsoleUI.PrintSuccess($"Items appended to {orderId}: {itemsJson}");

            createdOrders.Add(new CreatedOrder { OrderId = orderId, City = demand.City });
        }

        ConsoleUI.PrintSuccess($"All {createdOrders.Count} orders created and filled.");

        // ══════════════════════════════════════════════════════════════
        // Phase 5: Verify and Submit
        // ══════════════════════════════════════════════════════════════
        ConsoleUI.PrintPhase(5, "Verify and Submit");
        _logger.LogPhase(5, "Verify and Submit");

        var finalOrders = await _tools.OrdersGet();
        _logger.LogInfo($"Final orders state:\n{finalOrders}");
        ConsoleUI.PrintInfo("Final orders state logged.");

        var doneResponse = await _tools.Done();
        _logger.LogInfo($"Done response: {doneResponse}");

        return doneResponse;
    }

    // ──────────────────────────────────────────────────────────────────
    // Parsing helpers
    // ──────────────────────────────────────────────────────────────────

    private static List<CityDemand> ParseCityDemands(string json)
    {
        var result = new List<CityDemand>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Object)
        {
            // Format: { "opalino": { "chleb": 10, ... }, ... }
            foreach (var cityProp in root.EnumerateObject())
            {
                var demand = new CityDemand { City = cityProp.Name };
                if (cityProp.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var item in cityProp.Value.EnumerateObject())
                    {
                        if (item.Value.TryGetInt32(out var qty))
                            demand.Items[item.Name] = qty;
                    }
                }
                result.Add(demand);
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in root.EnumerateArray())
            {
                var demand = new CityDemand();
                if (element.TryGetProperty("city", out var c)) demand.City = c.GetString() ?? "";
                else if (element.TryGetProperty("name", out var n)) demand.City = n.GetString() ?? "";

                if (element.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
                    foreach (var item in items.EnumerateObject())
                        if (item.Value.TryGetInt32(out var qty))
                            demand.Items[item.Name] = qty;

                if (!string.IsNullOrWhiteSpace(demand.City))
                    result.Add(demand);
            }
        }

        return result;
    }

    /// <summary>
    /// Parse "show tables" response.
    /// The API returns { "tables": ["table1", "table2", ...] }
    /// </summary>
    private static List<string> ParseShowTablesResponse(string response)
    {
        var tables = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            // API returns { "tables": [...] }
            if (root.TryGetProperty("tables", out var tablesProp) &&
                tablesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in tablesProp.EnumerateArray())
                {
                    var name = item.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                        tables.Add(name);
                }
            }
            // Fallback: { "reply": [...] }
            else if (root.TryGetProperty("reply", out var reply) &&
                     reply.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in reply.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        tables.Add(item.GetString()!);
                    }
                    else if (item.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in item.EnumerateObject())
                        {
                            var val = prop.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(val))
                                tables.Add(val);
                        }
                    }
                }
            }
        }
        catch { }

        return tables;
    }

    /// <summary>Extract destinations from a single API response string</summary>
    private Dictionary<string, int> ExtractDestinationsFromResponse(string data, Dictionary<string, int> existing)
    {
        var result = new Dictionary<string, int>(existing, StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            JsonElement rows;
            if (root.TryGetProperty("rows", out var rp)) rows = rp;
            else if (root.TryGetProperty("reply", out var rep)) rows = rep;
            else return result;

            if (rows.ValueKind != JsonValueKind.Array) return result;

            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                var props = row.EnumerateObject().ToList();
                string? cityName = null;
                int? code = null;

                foreach (var prop in props)
                {
                    var key = prop.Name.ToLowerInvariant();
                    if (key is "name" or "city" or "miasto" or "nazwa" or "town")
                        cityName = prop.Value.GetString();
                }
                foreach (var prop in props)
                {
                    var key = prop.Name.ToLowerInvariant();
                    bool isCodeCol = key is "destination_id" or "code" or "kod" or "dest_code" or "destination_code"
                        || (key.EndsWith("_id") && !key.StartsWith("role") && !key.StartsWith("user"));
                    if (!isCodeCol) continue;
                    if (prop.Value.TryGetInt32(out var n)) { code = n; break; }
                    if (prop.Value.ValueKind == JsonValueKind.String &&
                        int.TryParse(prop.Value.GetString(), out var n2)) { code = n2; break; }
                }
                if (!string.IsNullOrWhiteSpace(cityName) && code.HasValue)
                {
                    result[cityName] = code.Value;
                    _logger.LogInfo($"Destination mapped: '{cityName}' → {code.Value}");
                }
            }
        }
        catch { }
        return result;
    }

    /// <summary>
    /// Extract destination codes from table data.
    /// Returns Dictionary mapping city_name → numeric_destination_code.
    /// Table structure: destination_id (int), name (string)
    /// </summary>
    private Dictionary<string, int> ExtractDestinations(Dictionary<string, string> tableData)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Try the "destinations" table first
        var orderedTables = tableData.Keys
            .OrderByDescending(k => k.ToLowerInvariant().Contains("destination") || k.ToLowerInvariant().Contains("dest"))
            .ToList();

        foreach (var tableName in orderedTables)
        {
            result = ExtractDestinationsFromResponse(tableData[tableName], result);
            if (result.Count > 0) break;
        }

        return result;
    }

    private static int GetDestination(Dictionary<string, int> dict, string city)
    {
        foreach (var (k, v) in dict)
            if (string.Equals(k, city, StringComparison.OrdinalIgnoreCase))
                return v;
        throw new KeyNotFoundException($"Destination not found for city: {city}");
    }

    private record CreatorInfo(int Id, string Login, string Birthday, int RoleId = 0);

    /// <summary>
    /// Find which role_id corresponds to transport/delivery role in the roles table.
    /// Looks for role names containing "transport" (case-insensitive).
    /// </summary>
    private int ExtractTransportRoleId(Dictionary<string, string> tableData)
    {
        foreach (var (tableName, data) in tableData)
        {
            if (!tableName.ToLowerInvariant().Contains("role")) continue;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                JsonElement rows;
                if (root.TryGetProperty("rows", out var rp)) rows = rp;
                else if (root.TryGetProperty("reply", out var rep)) rows = rep;
                else continue;

                if (rows.ValueKind != JsonValueKind.Array) continue;

                foreach (var row in rows.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object) continue;
                    int roleId = 0;
                    string? name = null;
                    foreach (var prop in row.EnumerateObject())
                    {
                        var key = prop.Name.ToLowerInvariant();
                        if (key is "role_id" or "id")
                            prop.Value.TryGetInt32(out roleId);
                        else if (key is "name" or "role_name")
                            name = prop.Value.GetString();
                    }
                    if (roleId > 0 && name != null &&
                        (name.Contains("transport", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("dostaw", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("obsług", StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogInfo($"Transport role found: id={roleId}, name={name}");
                        return roleId;
                    }
                }
            }
            catch { }
        }
        return 0;
    }

    /// <summary>
    /// Extract creator info from the users table.
    /// Users table columns: user_id, login, name_surname, password, birthday, role, is_active
    /// Prefers users whose role matches transportRoleId (if > 0).
    /// </summary>
    private CreatorInfo ExtractCreatorInfo(Dictionary<string, string> tableData, int transportRoleId = 0)
    {
        // Try "users" table first
        var orderedTables = tableData.Keys
            .OrderByDescending(k => k.ToLowerInvariant().Contains("user") || k.ToLowerInvariant().Contains("creator"))
            .ToList();

        CreatorInfo? fallback = null;

        foreach (var tableName in orderedTables)
        {
            var data = tableData[tableName];
            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;

                // API returns rows in "rows" key for SELECT queries
                JsonElement rows;
                if (root.TryGetProperty("rows", out var rowsProp))
                    rows = rowsProp;
                else if (root.TryGetProperty("reply", out var reply))
                    rows = reply;
                else
                    continue;

                if (rows.ValueKind != JsonValueKind.Array) continue;

                foreach (var row in rows.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object) continue;

                    var props = row.EnumerateObject().ToList();

                    int id = 0;
                    string? login = null;
                    string? birthday = null;
                    int roleId = 0;
                    int isActive = 1;

                    foreach (var prop in props)
                    {
                        var key = prop.Name.ToLowerInvariant();
                        if (key is "id" or "user_id" or "userid" or "creator_id")
                            prop.Value.TryGetInt32(out id);
                        else if (key is "login" or "username" or "user_login")
                            login = prop.Value.GetString();
                        else if (key is "birthday" or "birth_date" or "date_of_birth" or "dob" or "born" or "data_urodzenia")
                        {
                            birthday = prop.Value.GetString();
                            if (birthday == null && prop.Value.TryGetDateTime(out var dt))
                                birthday = dt.ToString("yyyy-MM-dd");
                        }
                        else if (key is "role" or "role_id")
                            prop.Value.TryGetInt32(out roleId);
                        else if (key is "is_active" or "active" or "isactive")
                            prop.Value.TryGetInt32(out isActive);
                    }

                    if (id > 0 && !string.IsNullOrWhiteSpace(login) && !string.IsNullOrWhiteSpace(birthday))
                    {
                        _logger.LogInfo($"User candidate in '{tableName}': id={id}, login={login}, birthday={birthday}, role={roleId}, active={isActive}");
                        var candidate = new CreatorInfo(id, login, birthday, roleId);

                        // Prefer active user with transport role
                        if (transportRoleId > 0 && roleId == transportRoleId && isActive == 1)
                        {
                            _logger.LogInfo($"Creator found (transport role): id={id}, login={login}");
                            return candidate;
                        }

                        // Keep as fallback if no transport role preference
                        fallback ??= candidate;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("ExtractCreatorInfo", $"Table {tableName}: {ex.Message}");
            }
        }

        if (fallback != null)
        {
            _logger.LogInfo($"Creator fallback used: id={fallback.Id}, login={fallback.Login}");
            return fallback;
        }

        return new CreatorInfo(0, "", "");
    }

    /// <summary>Extract signature value from signatureGenerator response (field: "hash")</summary>
    private static string ExtractSignatureValue(string response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            // Primary: "hash" field
            if (root.TryGetProperty("hash", out var hash))
                return hash.GetString() ?? "";

            // Fallbacks
            if (root.TryGetProperty("signature", out var sig))
                return sig.GetString() ?? "";

            if (root.TryGetProperty("reply", out var reply))
            {
                if (reply.ValueKind == JsonValueKind.String)
                    return reply.GetString() ?? "";
            }
        }
        catch { }

        // Last resort: 40-char hex string
        var trimmed = response.Trim().Trim('"');
        if (trimmed.Length == 40 && trimmed.All(c => "0123456789abcdefABCDEF".Contains(c)))
            return trimmed;

        return "";
    }

    /// <summary>Extract order ID from orders.create response</summary>
    private static string ExtractOrderId(string response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            // Primary: nested under "order" object — {"order": {"id": "..."}}
            if (root.TryGetProperty("order", out var orderObj) &&
                orderObj.ValueKind == JsonValueKind.Object &&
                orderObj.TryGetProperty("id", out var oid))
                return oid.GetString() ?? oid.GetRawText().Trim('"');

            // Direct id field
            if (root.TryGetProperty("id", out var id))
                return id.GetString() ?? id.GetRawText().Trim('"');

            if (root.TryGetProperty("reply", out var reply))
            {
                if (reply.ValueKind == JsonValueKind.String)
                    return reply.GetString() ?? "";
                if (reply.ValueKind == JsonValueKind.Object && reply.TryGetProperty("id", out var rid))
                    return rid.GetString() ?? rid.GetRawText().Trim('"');
            }

            if (root.TryGetProperty("message", out var msg))
            {
                var m = msg.GetString() ?? "";
                if (m.Length > 0 && !m.Contains(' '))
                    return m;
            }
        }
        catch { }

        return "";
    }
}
