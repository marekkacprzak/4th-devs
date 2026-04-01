namespace Domatowo.Models;

/// <summary>A single cell on the 11x11 city map.</summary>
public record MapCell(int Col, int Row, string Symbol)
{
    /// <summary>Grid coordinate in "A1" notation (col letter + row number, 1-based).</summary>
    public string Coord => $"{(char)('A' + Col)}{Row + 1}";
}

/// <summary>Parsed representation of the full city map.</summary>
public class CityMap
{
    public int Width { get; init; }
    public int Height { get; init; }
    public MapCell[,] Grid { get; init; } = new MapCell[0, 0];

    /// <summary>Cells reachable by transporter (road/street symbols).</summary>
    public List<MapCell> Roads { get; init; } = [];

    /// <summary>Candidate cells where the survivor may be hiding (tall buildings).</summary>
    public List<MapCell> TallBuildingCells { get; init; } = [];

    /// <summary>All non-road, non-obstacle cells (buildings of any height).</summary>
    public List<MapCell> BuildingCells { get; init; } = [];

    public MapCell? GetCell(int col, int row)
    {
        if (col < 0 || col >= Width || row < 0 || row >= Height) return null;
        return Grid[col, row];
    }
}

/// <summary>A unit (transporter or scout) currently active in the mission.</summary>
public class UnitState
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = ""; // "transporter" | "scout"
    public int Col { get; set; }
    public int Row { get; set; }
    public List<string> PassengerIds { get; set; } = [];

    public string Coord => $"{(char)('A' + Col)}{Row + 1}";
}

/// <summary>Budget tracker for the 300 action-point limit.</summary>
public class Budget
{
    public int Total { get; } = 300;
    public int Spent { get; private set; }
    public int Remaining => Total - Spent;

    public bool CanAfford(int cost) => Remaining >= cost;

    public void Charge(int cost) => Spent += cost;

    public override string ToString() => $"{Remaining}/{Total} pts remaining";
}

/// <summary>Result from parsing an API response for a move/inspect action.</summary>
public record ActionResult(bool Success, string RawResponse, string? SurvivorFoundAt = null);

/// <summary>A step in a planned route (col, row of each cell to visit).</summary>
public record RouteStep(int Col, int Row)
{
    public string Coord => $"{(char)('A' + Col)}{Row + 1}";
}
