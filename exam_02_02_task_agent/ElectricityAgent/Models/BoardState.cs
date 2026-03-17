using System.Text;

namespace ElectricityAgent.Models;

[Flags]
public enum CableEdge
{
    None   = 0,
    Top    = 1,
    Right  = 2,
    Bottom = 4,
    Left   = 8
}

public class GridTile
{
    public int Row { get; set; }
    public int Column { get; set; }
    public CableEdge Connections { get; set; }
    public string? Label { get; set; }

    public string Address => $"{Row}x{Column}";

    /// <summary>
    /// Returns connections after one 90-degree clockwise rotation.
    /// Top->Right, Right->Bottom, Bottom->Left, Left->Top
    /// </summary>
    public static CableEdge RotateClockwise(CableEdge connections)
    {
        var result = CableEdge.None;
        if (connections.HasFlag(CableEdge.Top)) result |= CableEdge.Right;
        if (connections.HasFlag(CableEdge.Right)) result |= CableEdge.Bottom;
        if (connections.HasFlag(CableEdge.Bottom)) result |= CableEdge.Left;
        if (connections.HasFlag(CableEdge.Left)) result |= CableEdge.Top;
        return result;
    }

    /// <summary>
    /// How many 90-degree clockwise rotations are needed to go from current to target connections.
    /// Returns 0-3, or -1 if the connections don't match any rotation.
    /// </summary>
    public static int RotationsNeeded(CableEdge current, CableEdge target)
    {
        var rotated = current;
        for (int i = 0; i < 4; i++)
        {
            if (rotated == target) return i;
            rotated = RotateClockwise(rotated);
        }
        return -1; // connections don't match any rotation (different cable pattern)
    }

    public override string ToString()
    {
        var edges = new List<string>();
        if (Connections.HasFlag(CableEdge.Top)) edges.Add("T");
        if (Connections.HasFlag(CableEdge.Right)) edges.Add("R");
        if (Connections.HasFlag(CableEdge.Bottom)) edges.Add("B");
        if (Connections.HasFlag(CableEdge.Left)) edges.Add("L");
        var label = Label != null ? $" [{Label}]" : "";
        return $"{Address}: {string.Join(",", edges)}{label}";
    }
}

public class BoardState
{
    public GridTile[,] Tiles { get; set; } = new GridTile[3, 3];

    public GridTile GetTile(int row, int col) => Tiles[row - 1, col - 1];
    public void SetTile(int row, int col, GridTile tile) => Tiles[row - 1, col - 1] = tile;

    public string ToTextDescription()
    {
        var sb = new StringBuilder();
        for (int row = 1; row <= 3; row++)
        {
            for (int col = 1; col <= 3; col++)
            {
                var tile = GetTile(row, col);
                if (tile != null)
                    sb.AppendLine(tile.ToString());
                else
                    sb.AppendLine($"{row}x{col}: (unknown)");
            }
        }
        return sb.ToString().TrimEnd();
    }

    public string ToGridView()
    {
        var sb = new StringBuilder();
        for (int row = 1; row <= 3; row++)
        {
            var line = new List<string>();
            for (int col = 1; col <= 3; col++)
            {
                var tile = GetTile(row, col);
                if (tile == null) { line.Add("???"); continue; }

                var t = tile.Connections.HasFlag(CableEdge.Top) ? "|" : " ";
                var b = tile.Connections.HasFlag(CableEdge.Bottom) ? "|" : " ";
                var l = tile.Connections.HasFlag(CableEdge.Left) ? "-" : " ";
                var r = tile.Connections.HasFlag(CableEdge.Right) ? "-" : " ";
                line.Add($" {t} ");
                line.Add($"{l}+{r}");
                line.Add($" {b} ");
            }
            // Print 3 sub-rows for each grid row
            for (int subRow = 0; subRow < 3; subRow++)
            {
                for (int col = 0; col < 3; col++)
                {
                    if (col > 0) sb.Append("  ");
                    sb.Append(line[col * 3 + subRow]);
                }
                sb.AppendLine();
            }
            if (row < 3) sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}
