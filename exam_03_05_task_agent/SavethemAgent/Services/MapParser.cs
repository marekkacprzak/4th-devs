using System.Text.Json;
using SavethemAgent.Models;

namespace SavethemAgent.Services;

/// <summary>
/// Parses various JSON response shapes from the hub tools into a GridMap.
/// Handles both structured JSON objects and plain text grids.
///
/// Expected shapes (examples):
/// { "map": [[0,0,1,...], ...], "start": [0,0], "end": [9,9] }
/// { "grid": "S.........\n..........\n..........#....\nG", "width": 10, "height": 10 }
/// Plain text grid where S=start, G=goal, #/.=obstacle/walkable
/// </summary>
public static class MapParser
{
    private static readonly HashSet<string> ObstacleKeywords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "wall", "obstacle", "blocked", "impassable"
        };

    private static readonly HashSet<string> WaterKeywords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "water", "river", "lake", "sea", "ocean"
        };

    // Rough terrain: navigable only for horse/walk (not for rocket/car)
    private static readonly HashSet<string> RoughKeywords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "rock", "stone", "tree", "forest", "mountain", "rough", "cliff"
        };

    public static GridMap Parse(string json)
    {
        var map = new GridMap();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Try structured map array: { "map": [[...], ...] }
            if (TryParseMapArray(root, map)) return map;

            // Try { "grid": "text..." }
            if (root.TryGetProperty("grid", out var gridProp) && gridProp.ValueKind == JsonValueKind.String)
            {
                ParseTextGrid(gridProp.GetString()!, map);
                return map;
            }

            // Try array of objects with terrain types
            if (root.ValueKind == JsonValueKind.Array)
            {
                ParseCellArray(root, map);
                return map;
            }

            // Try { "data": [...] } or { "board": "..." }
            if (root.TryGetProperty("data", out var dataProp))
            {
                if (dataProp.ValueKind == JsonValueKind.Array)
                {
                    ParseCellArray(dataProp, map);
                    return map;
                }
            }

            if (root.TryGetProperty("board", out var boardProp) && boardProp.ValueKind == JsonValueKind.String)
            {
                ParseTextGrid(boardProp.GetString()!, map);
                return map;
            }

            // Try to extract start/goal positions from any shape
            TryParsePositions(root, map);
        }
        catch (JsonException)
        {
            // Not valid JSON — try as plain text grid
            ParseTextGrid(json, map);
        }

        return map;
    }

    private static bool TryParseMapArray(JsonElement root, GridMap map)
    {
        // Shape: { "map": [[0,1,...], ...], "start": {"row":0,"col":0}, "end": {"row":9,"col":9} }
        JsonElement mapElem;
        if (!root.TryGetProperty("map", out mapElem) || mapElem.ValueKind != JsonValueKind.Array)
            return false;

        var rows = mapElem.GetArrayLength();
        for (int r = 0; r < rows && r < GridMap.Height; r++)
        {
            var rowElem = mapElem[r];
            if (rowElem.ValueKind != JsonValueKind.Array) continue;
            var cols = rowElem.GetArrayLength();
            for (int c = 0; c < cols && c < GridMap.Width; c++)
            {
                var cell = rowElem[c];
                if (cell.ValueKind == JsonValueKind.String)
                {
                    var ch = cell.GetString() ?? ".";
                    switch (ch)
                    {
                        case "S": case "s":
                            map.StartPosition = (r, c);
                            map.Cells[r, c] = CellType.Walkable;
                            break;
                        case "G": case "g": case "E": case "e":
                            map.GoalPosition = (r, c);
                            map.Cells[r, c] = CellType.Walkable;
                            break;
                        case "W": case "~":
                            map.Cells[r, c] = CellType.Water;
                            break;
                        case "T": case "R":
                            map.Cells[r, c] = CellType.Rough;
                            break;
                        case "#": case "X":
                            map.Cells[r, c] = CellType.Obstacle;
                            break;
                        default:
                            map.Cells[r, c] = IsObstacleValue(cell) ? CellType.Obstacle : CellType.Walkable;
                            break;
                    }
                }
                else
                {
                    map.Cells[r, c] = IsObstacleValue(cell) ? CellType.Obstacle : CellType.Walkable;
                }
            }
        }

        TryParsePositions(root, map);
        return true;
    }

    private static void ParseCellArray(JsonElement array, GridMap map)
    {
        // Flat array of cell objects: [{ "x": 0, "y": 0, "type": "wall" }, ...]
        foreach (var cell in array.EnumerateArray())
        {
            int row = 0, col = 0;
            if (cell.TryGetProperty("row", out var rp)) row = rp.GetInt32();
            else if (cell.TryGetProperty("y", out var yp)) row = yp.GetInt32();

            if (cell.TryGetProperty("col", out var cp)) col = cp.GetInt32();
            else if (cell.TryGetProperty("column", out var cp2)) col = cp2.GetInt32();
            else if (cell.TryGetProperty("x", out var xp)) col = xp.GetInt32();

            if (row < 0 || row >= GridMap.Height || col < 0 || col >= GridMap.Width) continue;

            if (cell.TryGetProperty("type", out var typeProp))
            {
                var typeStr = typeProp.GetString() ?? "";
                if (ObstacleKeywords.Contains(typeStr))
                    map.Cells[row, col] = CellType.Obstacle;
                else if (WaterKeywords.Contains(typeStr))
                    map.Cells[row, col] = CellType.Water;
                else if (RoughKeywords.Contains(typeStr))
                    map.Cells[row, col] = CellType.Rough;
                else if (typeStr.Equals("start", StringComparison.OrdinalIgnoreCase))
                    map.StartPosition = (row, col);
                else if (typeStr.Equals("goal", StringComparison.OrdinalIgnoreCase) ||
                         typeStr.Equals("end", StringComparison.OrdinalIgnoreCase) ||
                         typeStr.Equals("finish", StringComparison.OrdinalIgnoreCase))
                    map.GoalPosition = (row, col);
            }

            if (cell.TryGetProperty("value", out var valProp))
            {
                if (IsObstacleValue(valProp))
                    map.Cells[row, col] = CellType.Obstacle;
            }
        }
    }

    private static void ParseTextGrid(string text, GridMap map)
    {
        var lines = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        // Take up to 10 rows
        int row = 0;
        foreach (var line in lines)
        {
            if (row >= GridMap.Height) break;
            // Skip header/legend lines that don't look like grid rows
            if (line.StartsWith('#') && !line.Any(c => c == '.' || c == 'S' || c == 'G'))
                continue;

            for (int col = 0; col < line.Length && col < GridMap.Width; col++)
            {
                char ch = line[col];
                switch (ch)
                {
                    case 'S':
                    case 's':
                        map.StartPosition = (row, col);
                        map.Cells[row, col] = CellType.Walkable;
                        break;
                    case 'G':
                    case 'g':
                    case 'E':
                    case 'e':
                        map.GoalPosition = (row, col);
                        map.Cells[row, col] = CellType.Walkable;
                        break;
                    case 'W':
                    case '~':
                        map.Cells[row, col] = CellType.Water;
                        break;
                    case 'T':
                    case 'R':
                        map.Cells[row, col] = CellType.Walkable;
                        break;
                    case '#':
                    case 'X':
                        map.Cells[row, col] = CellType.Obstacle;
                        break;
                    default:
                        map.Cells[row, col] = CellType.Walkable;
                        break;
                }
            }
            row++;
        }
    }

    private static void TryParsePositions(JsonElement root, GridMap map)
    {
        // start: { "row": 0, "col": 0 } or [row, col] or { "x": 0, "y": 0 }
        if (root.TryGetProperty("start", out var startProp))
            map.StartPosition = ParsePosition(startProp) ?? map.StartPosition;

        foreach (var goalKey in new[] { "end", "goal", "finish", "target" })
        {
            if (root.TryGetProperty(goalKey, out var goalProp))
            {
                map.GoalPosition = ParsePosition(goalProp) ?? map.GoalPosition;
                break;
            }
        }
    }

    private static (int row, int col)? ParsePosition(JsonElement elem)
    {
        if (elem.ValueKind == JsonValueKind.Array && elem.GetArrayLength() >= 2)
            return (elem[0].GetInt32(), elem[1].GetInt32());

        if (elem.ValueKind == JsonValueKind.Object)
        {
            int r = 0, c = 0;
            if (elem.TryGetProperty("row", out var rp)) r = rp.GetInt32();
            else if (elem.TryGetProperty("y", out var yp)) r = yp.GetInt32();
            if (elem.TryGetProperty("col", out var cp)) c = cp.GetInt32();
            else if (elem.TryGetProperty("column", out var cp2)) c = cp2.GetInt32();
            else if (elem.TryGetProperty("x", out var xp)) c = xp.GetInt32();
            return (r, c);
        }

        return null;
    }

    private static bool IsObstacleValue(JsonElement elem)
    {
        if (elem.ValueKind == JsonValueKind.Number)
            return elem.GetInt32() != 0;
        if (elem.ValueKind == JsonValueKind.String)
            return ObstacleKeywords.Contains(elem.GetString() ?? "");
        if (elem.ValueKind == JsonValueKind.True)
            return true;
        return false;
    }
}
