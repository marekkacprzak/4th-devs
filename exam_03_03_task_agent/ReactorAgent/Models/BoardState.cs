namespace ReactorAgent.Models;

public enum Direction { Up, Down }

public class ReactorBlock
{
    public int Column { get; set; }       // 1-based column (1–7)
    public int TopRow { get; set; }       // top cell row, 1-based (1–5)
    public int BottomRow => TopRow + 1;   // block occupies TopRow and TopRow+1
    public Direction MoveDirection { get; set; }
}

public class ReactorBoard
{
    public const int Width = 7;
    public const int Height = 5;

    public int RobotColumn { get; set; } = 1;   // robot always on row 5
    public List<ReactorBlock> Blocks { get; set; } = new();
    public bool IsGoalReached => RobotColumn == Width;

    /// <summary>Returns true if (col, row) is currently occupied by a reactor block.</summary>
    public bool IsCellOccupied(int col, int row)
    {
        return Blocks.Any(b => b.Column == col && (b.TopRow == row || b.BottomRow == row));
    }

    /// <summary>Renders the board as a 7×5 text grid for display/logging.</summary>
    public string ToTextGrid()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"  1234567");
        for (int row = 1; row <= Height; row++)
        {
            sb.Append($"{row} ");
            for (int col = 1; col <= Width; col++)
            {
                if (col == RobotColumn && row == Height)
                    sb.Append('R');
                else if (col == Width && row == Height)
                    sb.Append('G');
                else if (col == 1 && row == Height && RobotColumn != 1)
                    sb.Append('P');
                else if (IsCellOccupied(col, row))
                    sb.Append('B');
                else
                    sb.Append('.');
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
