using System.Text;

namespace SavethemAgent.Models;

public enum CellType { Walkable, Rough, Water, Obstacle }

public class GridMap
{
    public const int Width = 10;
    public const int Height = 10;

    // Cells[row, col] — 0-based indexing
    // Water = W (passable only for water-crossing vehicles)
    // Obstacle = truly impassable solid terrain (not used currently — T and R are walkable per vehicle notes)
    // Walkable = normal ground (also T/R terrain that all vehicles can traverse)
    public CellType[,] Cells { get; set; } = new CellType[Height, Width];

    public (int row, int col) StartPosition { get; set; } = (0, 0);
    public (int row, int col) GoalPosition { get; set; } = (9, 9);

    public bool IsWalkable(int row, int col, bool canCrossWater = false, bool canCrossRough = false)
    {
        if (row < 0 || row >= Height || col < 0 || col >= Width)
            return false;
        return Cells[row, col] switch
        {
            CellType.Walkable => true,
            CellType.Rough => canCrossRough,
            CellType.Water => canCrossWater,
            CellType.Obstacle => false,
            _ => false
        };
    }

    public string ToTextGrid()
    {
        var sb = new StringBuilder();
        sb.AppendLine("   0123456789");
        var (sr, sc) = StartPosition;
        var (gr, gc) = GoalPosition;
        for (int r = 0; r < Height; r++)
        {
            sb.Append($"{r,2} ");
            for (int c = 0; c < Width; c++)
            {
                if (r == sr && c == sc)
                    sb.Append('S');
                else if (r == gr && c == gc)
                    sb.Append('G');
                else
                    sb.Append(Cells[r, c] switch
                    {
                        CellType.Rough => '^',
                        CellType.Water => '~',
                        CellType.Obstacle => '#',
                        _ => '.'
                    });
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
