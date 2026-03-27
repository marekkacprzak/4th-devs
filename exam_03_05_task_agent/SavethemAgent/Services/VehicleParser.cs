using System.Text.Json;
using System.Text.RegularExpressions;
using SavethemAgent.Models;

namespace SavethemAgent.Services;

/// <summary>
/// Parses vehicle data from various JSON response shapes.
/// Handles both structured fields and natural-language "note" descriptions.
/// </summary>
public static class VehicleParser
{
    // Fuel patterns: "Fuel consumption is 0.5", "Fuel consumption: 2.0", "0.5 fuel per step"
    private static readonly Regex FuelRegex = new(
        @"[Ff]uel\s+consumption\s+(?:per\s+\w+\s+)?(?:is\s+)?:?\s*(\d+\.?\d*)" +
        @"|(\d+\.?\d*)\s+fuel\s+per\s+step" +
        @"|[Ff]uel(?:[^:.\d]{0,20}):?\s*(\d+\.?\d*)",
        RegexOptions.IgnoreCase);

    // Food patterns: "Food consumption is 0.5", "food per step is 1.0", "0.2 food per step"
    private static readonly Regex FoodRegex = new(
        @"[Ff]ood\s+consumption\s+(?:per\s+\w+\s+)?(?:is\s+)?:?\s*(\d+\.?\d*)" +
        @"|(\d+\.?\d*)\s+food\s+per\s+step" +
        @"|[Ff]ood\s+per\s+step\s+(?:is\s+)?:?\s*(\d+\.?\d*)",
        RegexOptions.IgnoreCase);

    public static List<Vehicle> Parse(string json)
    {
        var vehicles = new List<Vehicle>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Try { "vehicles": [...] } or { "data": [...] } or { "tools": [...] }
            foreach (var key in new[] { "vehicles", "data", "transport", "options", "tools" })
            {
                if (root.TryGetProperty(key, out var arrProp) && arrProp.ValueKind == JsonValueKind.Array)
                {
                    ParseVehicleArray(arrProp, vehicles);
                    if (vehicles.Count > 0) return vehicles;
                }
            }

            // Try root array
            if (root.ValueKind == JsonValueKind.Array)
            {
                ParseVehicleArray(root, vehicles);
                return vehicles;
            }

            // Try single vehicle object (e.g. { "name": "rocket", "note": "..." })
            if (root.ValueKind == JsonValueKind.Object)
            {
                var v = ParseSingleVehicle(root);
                if (v != null) vehicles.Add(v);
            }
        }
        catch (JsonException)
        {
            // Not JSON - ignore
        }

        return vehicles;
    }

    private static void ParseVehicleArray(JsonElement array, List<Vehicle> vehicles)
    {
        foreach (var elem in array.EnumerateArray())
        {
            if (elem.ValueKind == JsonValueKind.Object)
            {
                var v = ParseSingleVehicle(elem);
                if (v != null)
                    vehicles.Add(v);
            }
        }
    }

    // Water restriction patterns: "cannot travel over water", "cannot drive on water", etc.
    private static readonly Regex CannotCrossWaterRegex = new(
        @"cannot\s+(?:travel|drive|go|move|cross|traverse|walk)\s+(?:over|on|through|across)?\s*water",
        RegexOptions.IgnoreCase);

    private static readonly Regex CanCrossWaterRegex = new(
        @"(?:can|able\s+to)\s+(?:travel|drive|go|move|cross|traverse|walk)\s+(?:over|on|through|across)?\s*water",
        RegexOptions.IgnoreCase);

    private static Vehicle? ParseSingleVehicle(JsonElement elem)
    {
        var name = GetStringProp(elem, "name", "vehicle", "id", "type");
        if (string.IsNullOrEmpty(name)) return null;

        // Try structured numeric fields first
        double? fuel = GetDoubleProp(elem,
            "fuelPerStep", "fuel_per_step", "fuelConsumption",
            "fuel_consumption", "fuel");

        double? food = GetDoubleProp(elem,
            "foodPerStep", "food_per_step", "foodConsumption",
            "food_consumption", "food");

        // Try { "consumption": { "fuel": X, "food": X } } nested object
        if ((!fuel.HasValue || !food.HasValue) &&
            elem.TryGetProperty("consumption", out var consumption) &&
            consumption.ValueKind == JsonValueKind.Object)
        {
            if (!fuel.HasValue) fuel = GetDoubleProp(consumption, "fuel", "fuelPerStep", "fuel_per_step");
            if (!food.HasValue) food = GetDoubleProp(consumption, "food", "foodPerStep", "food_per_step");
        }

        // If still not found, extract from "note" text
        var note = GetStringProp(elem, "note", "description", "desc", "info") ?? "";
        if (!fuel.HasValue || !food.HasValue)
        {
            if (note.Length > 0)
            {
                if (!fuel.HasValue) fuel = ExtractNumberFromText(FuelRegex, note);
                if (!food.HasValue) food = ExtractNumberFromText(FoodRegex, note);
            }
        }

        // Apply walking heuristic: if name suggests walking, default fuel=0
        bool isWalking = name.Equals("walk", StringComparison.OrdinalIgnoreCase) ||
                         name.Equals("walking", StringComparison.OrdinalIgnoreCase) ||
                         name.Equals("on_foot", StringComparison.OrdinalIgnoreCase);

        bool isHorseOrWalk = isWalking || name.Equals("horse", StringComparison.OrdinalIgnoreCase);

        // Detect water traversal capability from note
        bool canCrossWater;
        if (CannotCrossWaterRegex.IsMatch(note))
            canCrossWater = false;
        else if (CanCrossWaterRegex.IsMatch(note))
            canCrossWater = true;
        else
            canCrossWater = isHorseOrWalk; // heuristic: horse/walk can cross water

        // Rough terrain (rocks, trees): only horse and walk can traverse
        // Rocket explicitly can't fly over "chasm, cliff, or other gap" — treated as rough terrain
        bool canCrossRough = isHorseOrWalk ||
                             (note.Length > 0 && Regex.IsMatch(note,
                                 @"can\s+(?:cross|traverse|handle|navigate)\s+(?:rough|rock|tree|forest)",
                                 RegexOptions.IgnoreCase));

        return new Vehicle
        {
            Name = name,
            FuelPerStep = fuel ?? (isWalking ? 0.0 : 1.0),
            FoodPerStep = food ?? 1.0,
            CanCrossWater = canCrossWater,
            CanCrossRough = canCrossRough
        };
    }

    private static double? ExtractNumberFromText(Regex regex, string text)
    {
        var match = regex.Match(text);
        if (!match.Success) return null;

        // Find first non-empty capture group
        for (int i = 1; i < match.Groups.Count; i++)
        {
            if (match.Groups[i].Success && !string.IsNullOrEmpty(match.Groups[i].Value))
            {
                if (double.TryParse(match.Groups[i].Value,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var val))
                    return val;
            }
        }
        return null;
    }

    private static string? GetStringProp(JsonElement elem, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (elem.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }
        return null;
    }

    private static double? GetDoubleProp(JsonElement elem, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (elem.TryGetProperty(key, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                    return prop.GetDouble();
                if (prop.ValueKind == JsonValueKind.String &&
                    double.TryParse(prop.GetString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var d))
                    return d;
            }
        }
        return null;
    }
}
