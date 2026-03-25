using System.Diagnostics;
using System.Text.Json;
using ReactorAgent.Models;

namespace ReactorAgent.Services;

public class ReactorNavigator
{
    private static readonly ActivitySource Activity = new("ReactorAgent.Navigator");

    /// <summary>
    /// Parse the raw JSON response from the Hub API into a ReactorBoard.
    /// The API returns a JSON object with a "board" field containing a text grid,
    /// and block direction metadata.
    ///
    /// Expected response shape (inferred from task description):
    /// {
    ///   "code": 0,
    ///   "message": "OK",
    ///   "board": ".......\n.......\n.......\nBBBB...\nBBBB...",
    ///   "description": "...",
    ///   "blocks": [
    ///     { "col": 1, "topRow": 4, "direction": "up" },
    ///     ...
    ///   ]
    /// }
    ///
    /// If the exact shape differs, fall back to parsing the grid text only (no direction info).
    /// </summary>
    public ReactorBoard ParseBoard(string apiResponseJson)
    {
        using var span = Activity.StartActivity("navigator.parse_board");
        var board = new ReactorBoard();

        try
        {
            using var doc = JsonDocument.Parse(apiResponseJson);
            var root = doc.RootElement;

            // Parse robot position from board text if available
            string? boardText = null;
            if (root.TryGetProperty("board", out var boardProp))
            {
                if (boardProp.ValueKind == JsonValueKind.String)
                    boardText = boardProp.GetString();
                // If it's a 2D array ([[".","B",...], ...]), blocks will be parsed from the
                // structured "blocks" array instead (handled below).
                // We don't need to parse the board grid if structured block data is present.
            }

            // Try structured blocks array first
            if (root.TryGetProperty("blocks", out var blocksProp) && blocksProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var blockElem in blocksProp.EnumerateArray())
                {
                    var block = new ReactorBlock();

                    if (blockElem.TryGetProperty("col", out var colProp))
                        block.Column = colProp.GetInt32();
                    else if (blockElem.TryGetProperty("column", out var colProp2))
                        block.Column = colProp2.GetInt32();

                    if (blockElem.TryGetProperty("topRow", out var topRowProp))
                        block.TopRow = topRowProp.GetInt32();
                    else if (blockElem.TryGetProperty("top_row", out var topRowProp2))
                        block.TopRow = topRowProp2.GetInt32();
                    else if (blockElem.TryGetProperty("row", out var rowProp))
                        block.TopRow = rowProp.GetInt32();

                    var dirStr = "";
                    if (blockElem.TryGetProperty("direction", out var dirProp))
                        dirStr = dirProp.GetString() ?? "";
                    else if (blockElem.TryGetProperty("dir", out var dirProp2))
                        dirStr = dirProp2.GetString() ?? "";

                    block.MoveDirection = dirStr.ToLowerInvariant() switch
                    {
                        "up" or "u" => Direction.Up,
                        _ => Direction.Down
                    };

                    if (block.Column > 0 && block.TopRow > 0)
                        board.Blocks.Add(block);
                }
            }
            else if (boardText != null)
            {
                // Fall back: parse grid text only, infer blocks from 'B' characters
                ParseBlocksFromGrid(board, boardText);
            }

            // Parse robot position from board text
            if (boardText != null)
                ParseRobotPosition(board, boardText);

            // Parse robot position from structured field if available
            // API returns: "player": { "col": 1, "row": 5 }
            if (root.TryGetProperty("player", out var playerProp) &&
                playerProp.TryGetProperty("col", out var playerColProp))
                board.RobotColumn = playerColProp.GetInt32();
            else if (root.TryGetProperty("robotCol", out var robotColProp))
                board.RobotColumn = robotColProp.GetInt32();
            else if (root.TryGetProperty("robot_col", out var robotColProp2))
                board.RobotColumn = robotColProp2.GetInt32();

            // Check reached_goal flag from API
            if (root.TryGetProperty("reached_goal", out var reachedGoalProp) &&
                reachedGoalProp.GetBoolean())
                board.RobotColumn = ReactorBoard.Width;
        }
        catch (JsonException)
        {
            // If not valid JSON, try treating the whole response as a board text grid
            ParseBlocksFromGrid(board, apiResponseJson);
            ParseRobotPosition(board, apiResponseJson);
        }

        span?.SetTag("robot.column", board.RobotColumn);
        span?.SetTag("blocks.count", board.Blocks.Count);
        return board;
    }

    private void ParseBlocksFromGrid(ReactorBoard board, string gridText)
    {
        var lines = gridText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Find lines that represent rows 1..5 (may have extra header/footer lines)
        var gridLines = lines
            .Select(l => l.TrimEnd())
            .Where(l => l.Length >= 7 || l.All(c => c is 'B' or '.' or 'P' or 'G' or 'R'))
            .ToList();

        // Take last 5 lines as the grid if more are present
        if (gridLines.Count > ReactorBoard.Height)
            gridLines = gridLines.TakeLast(ReactorBoard.Height).ToList();

        for (int row = 0; row < gridLines.Count && row < ReactorBoard.Height; row++)
        {
            var line = gridLines[row];
            for (int col = 0; col < line.Length && col < ReactorBoard.Width; col++)
            {
                if (line[col] == 'B')
                {
                    // Check if this column already has a block starting at previous row
                    var existingBlock = board.Blocks.FirstOrDefault(
                        b => b.Column == col + 1 && b.BottomRow == row + 1);
                    if (existingBlock == null)
                    {
                        // New block — direction unknown, default Down
                        board.Blocks.Add(new ReactorBlock
                        {
                            Column = col + 1,
                            TopRow = row + 1,
                            MoveDirection = Direction.Down
                        });
                    }
                }
            }
        }
    }

    private void ParseRobotPosition(ReactorBoard board, string gridText)
    {
        var lines = gridText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var gridLines = lines
            .Select(l => l.TrimEnd())
            .Where(l => l.Length >= 7 || l.All(c => c is 'B' or '.' or 'P' or 'G' or 'R'))
            .ToList();

        if (gridLines.Count > ReactorBoard.Height)
            gridLines = gridLines.TakeLast(ReactorBoard.Height).ToList();

        // Robot is always on the last row (row 5)
        if (gridLines.Count >= ReactorBoard.Height)
        {
            var lastRow = gridLines[ReactorBoard.Height - 1];
            for (int col = 0; col < lastRow.Length && col < ReactorBoard.Width; col++)
            {
                if (lastRow[col] == 'R' || lastRow[col] == 'P')
                {
                    // 'R' for current robot position; 'P' is start marker (col 1)
                    if (lastRow[col] == 'R')
                        board.RobotColumn = col + 1;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Decide the next safe command for the robot based on the current board state.
    /// Uses one-step lookahead: simulates where blocks will be AFTER the command is executed.
    ///
    /// Algorithm:
    ///   1. If goal reached → "done"
    ///   2. Simulate blocks one step. Check if col+1, row=5 is safe → "right"
    ///   3. Simulate blocks one step. Check if current col, row=5 is safe → "wait"
    ///   4. Simulate blocks one step. Check if col-1, row=5 is safe → "left"
    ///   5. Fallback → "wait"
    /// </summary>
    public string DecideNextMove(ReactorBoard board)
    {
        using var span = Activity.StartActivity("navigator.decide_next_move");
        span?.SetTag("robot.column", board.RobotColumn);

        if (board.IsGoalReached)
            return "done";

        // Simulate all blocks moving one step (as they will when any command is sent)
        var nextBlocks = SimulateOneStep(board.Blocks);

        bool rightSafe = board.RobotColumn < ReactorBoard.Width
            && !IsCellOccupiedByBlocks(nextBlocks, board.RobotColumn + 1, ReactorBoard.Height);

        bool currentSafe = !IsCellOccupiedByBlocks(nextBlocks, board.RobotColumn, ReactorBoard.Height);

        bool leftSafe = board.RobotColumn > 1
            && !IsCellOccupiedByBlocks(nextBlocks, board.RobotColumn - 1, ReactorBoard.Height);

        string command;
        if (rightSafe)
            command = "right";
        else if (currentSafe)
            command = "wait";
        else if (leftSafe)
            command = "left";
        else
            command = "wait"; // No safe option — wait and hope for the best

        span?.SetTag("decision", command);
        span?.SetTag("right.safe", rightSafe);
        span?.SetTag("current.safe", currentSafe);
        span?.SetTag("left.safe", leftSafe);

        return command;
    }

    private List<ReactorBlock> SimulateOneStep(List<ReactorBlock> blocks)
    {
        return blocks.Select(b => SimulateBlockMove(b)).ToList();
    }

    private ReactorBlock SimulateBlockMove(ReactorBlock block)
    {
        var next = new ReactorBlock
        {
            Column = block.Column,
            TopRow = block.TopRow,
            MoveDirection = block.MoveDirection
        };

        if (next.MoveDirection == Direction.Down)
        {
            next.TopRow++;
            // If bottom cell would exceed grid height, reverse
            if (next.BottomRow > ReactorBoard.Height)
            {
                next.TopRow -= 2; // step back
                next.MoveDirection = Direction.Up;
            }
        }
        else // Up
        {
            next.TopRow--;
            // If top cell goes above row 1, reverse
            if (next.TopRow < 1)
            {
                next.TopRow = 2; // step back to where we came from
                next.MoveDirection = Direction.Down;
            }
        }

        return next;
    }

    private bool IsCellOccupiedByBlocks(List<ReactorBlock> blocks, int col, int row)
    {
        return blocks.Any(b => b.Column == col && (b.TopRow == row || b.BottomRow == row));
    }
}
