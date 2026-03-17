using ElectricityAgent.Models;

namespace ElectricityAgent.Services;

/// <summary>
/// Deterministic backtracking solver for the 3x3 cable puzzle.
/// Finds rotations so that all internal edges match and power flows
/// from source (left of 3x1) to all three plants (right of 1x3, 2x3, 3x3).
/// </summary>
public static class PuzzleSolver
{
    public static List<(string tile, int rotations)> Solve(BoardState board)
    {
        var original = new CableEdge[3, 3];
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                original[r, c] = board.GetTile(r + 1, c + 1).Connections;

        var rotations = new int[3, 3];
        if (Backtrack(original, rotations, 0))
        {
            var result = new List<(string, int)>();
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    if (rotations[r, c] > 0)
                        result.Add(($"{r + 1}x{c + 1}", rotations[r, c]));
            return result;
        }

        return [];
    }

    private static bool Backtrack(CableEdge[,] original, int[,] rotations, int index)
    {
        if (index == 9) return true;

        int r = index / 3;
        int c = index % 3;

        for (int rot = 0; rot < 4; rot++)
        {
            rotations[r, c] = rot;
            var conn = ApplyRotation(original[r, c], rot);

            if (IsConsistent(original, rotations, r, c, conn))
            {
                if (Backtrack(original, rotations, index + 1))
                    return true;
            }
        }

        return false;
    }

    private static CableEdge ApplyRotation(CableEdge connections, int times)
    {
        var result = connections;
        for (int i = 0; i < times; i++)
            result = GridTile.RotateClockwise(result);
        return result;
    }

    private static bool IsConsistent(CableEdge[,] original, int[,] rotations, int r, int c, CableEdge conn)
    {
        // Check left neighbor matching
        if (c > 0)
        {
            var leftConn = ApplyRotation(original[r, c - 1], rotations[r, c - 1]);
            if (conn.HasFlag(CableEdge.Left) != leftConn.HasFlag(CableEdge.Right))
                return false;
        }

        // Check top neighbor matching
        if (r > 0)
        {
            var topConn = ApplyRotation(original[r - 1, c], rotations[r - 1, c]);
            if (conn.HasFlag(CableEdge.Top) != topConn.HasFlag(CableEdge.Bottom))
                return false;
        }

        // External boundary constraints:
        // Source enters from left of 3x1
        if (r == 2 && c == 0 && !conn.HasFlag(CableEdge.Left)) return false;

        // Plants on right edge — all row 1x3, 2x3, 3x3 must have Right
        if (c == 2 && !conn.HasFlag(CableEdge.Right)) return false;

        // No connections going off the top edge
        if (r == 0 && conn.HasFlag(CableEdge.Top)) return false;

        // No connections going off the bottom edge
        if (r == 2 && conn.HasFlag(CableEdge.Bottom)) return false;

        // No connections going off the left edge (except source at 3x1)
        if (c == 0 && r != 2 && conn.HasFlag(CableEdge.Left)) return false;

        return true;
    }
}
