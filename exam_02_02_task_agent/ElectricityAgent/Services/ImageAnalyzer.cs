using System.Diagnostics;
using System.Text;
using SkiaSharp;
using ElectricityAgent.Models;
using ElectricityAgent.UI;

namespace ElectricityAgent.Services;

/// <summary>
/// Programmatic pixel-based analysis of the electricity board PNG.
/// Detects cable connections by checking for dark pixels near each cell edge.
/// </summary>
public static class ImageAnalyzer
{
    private static readonly ActivitySource Activity = new("ElectricityAgent.ImageAnalyzer");
    private const int DarkThreshold = 120;
    private static string _dataDir = Path.Combine(Directory.GetCurrentDirectory(), "data");

    /// <summary>
    /// Analyze the board and save diagnostic files (PNG + recognition report) to data/.
    /// </summary>
    public static BoardState AnalyzeBoard(byte[] pngBytes, int stepNumber)
    {
        using var span = Activity.StartActivity("image.analyze_board");
        span?.SetTag("step_number", stepNumber);
        span?.SetTag("image.size_bytes", pngBytes.Length);
        // Ensure data directory exists
        Directory.CreateDirectory(_dataDir);

        // Save the raw board PNG
        var pngPath = Path.Combine(_dataDir, $"step_{stepNumber:D2}_board.png");
        File.WriteAllBytes(pngPath, pngBytes);
        ConsoleUI.PrintInfo($"Saved board image: {pngPath}");

        using var bitmap = SKBitmap.Decode(pngBytes);
        if (bitmap == null)
            throw new InvalidOperationException("Failed to decode board PNG");

        ConsoleUI.PrintInfo($"Analyzing image: {bitmap.Width}x{bitmap.Height}");

        // Find grid boundaries by scanning for concentrated dark pixel lines
        var (gridLeft, gridTop, gridRight, gridBottom) = FindGridBounds(bitmap);
        ConsoleUI.PrintInfo($"Grid bounds: ({gridLeft},{gridTop}) to ({gridRight},{gridBottom})");

        double cellW = (gridRight - gridLeft) / 3.0;
        double cellH = (gridBottom - gridTop) / 3.0;
        ConsoleUI.PrintInfo($"Cell size: {cellW:F0}x{cellH:F0}");

        // Build recognition report
        var report = new StringBuilder();
        report.AppendLine($"# Image Recognition Report - Step {stepNumber}");
        report.AppendLine($"Image: step_{stepNumber:D2}_board.png ({bitmap.Width}x{bitmap.Height})");
        report.AppendLine($"Grid bounds: ({gridLeft},{gridTop}) to ({gridRight},{gridBottom})");
        report.AppendLine($"Cell size: {cellW:F0}x{cellH:F0}");
        report.AppendLine();

        var board = new BoardState();
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                int cL = gridLeft + (int)(col * cellW);
                int cT = gridTop + (int)(row * cellH);
                int cR = gridLeft + (int)((col + 1) * cellW);
                int cB = gridTop + (int)((row + 1) * cellH);

                // Collect detailed sample data for report
                var edgeDetails = new Dictionary<CableEdge, (bool detected, double ratio, int cx, int cy)>();
                var connections = CableEdge.None;

                foreach (var edge in new[] { CableEdge.Top, CableEdge.Right, CableEdge.Bottom, CableEdge.Left })
                {
                    var (detected, ratio, sampleX, sampleY) = HasCableDetailed(bitmap, cL, cT, cR, cB, edge);
                    edgeDetails[edge] = (detected, ratio, sampleX, sampleY);
                    if (detected) connections |= edge;
                }

                var tile = new GridTile
                {
                    Row = row + 1,
                    Column = col + 1,
                    Connections = connections
                };

                board.SetTile(row + 1, col + 1, tile);
                ConsoleUI.PrintInfo($"  {tile}");

                // Save cropped tile image
                SaveCroppedTile(bitmap, cL, cT, cR, cB, stepNumber, row + 1, col + 1);

                // Write tile details to report
                report.AppendLine($"## Tile {tile.Address} (pixels: {cL},{cT} to {cR},{cB})");
                report.AppendLine($"  Result: {tile}");
                foreach (var (edge, (detected, ratio, sx, sy)) in edgeDetails)
                {
                    var edgeName = edge switch { CableEdge.Top => "Top", CableEdge.Right => "Right", CableEdge.Bottom => "Bottom", CableEdge.Left => "Left", _ => "?" };
                    report.AppendLine($"  {edgeName}: {(detected ? "YES" : "no ")} (dark ratio: {ratio:F3}, sample point: {sx},{sy})");
                }
                report.AppendLine();
            }
        }

        // Save the full board text description
        report.AppendLine("## Board Summary");
        report.AppendLine(board.ToTextDescription());
        report.AppendLine();
        report.AppendLine("## Grid View");
        report.AppendLine(board.ToGridView());

        var reportPath = Path.Combine(_dataDir, $"step_{stepNumber:D2}_recognition.md");
        File.WriteAllText(reportPath, report.ToString());
        ConsoleUI.PrintInfo($"Saved recognition report: {reportPath}");

        span?.SetTag("board.description", board.ToTextDescription());

        return board;
    }

    private static void SaveCroppedTile(SKBitmap bitmap, int cL, int cT, int cR, int cB, int step, int row, int col)
    {
        int w = cR - cL;
        int h = cB - cT;
        using var cropped = new SKBitmap(w, h);
        using var canvas = new SKCanvas(cropped);
        canvas.DrawBitmap(bitmap, new SKRect(cL, cT, cR, cB), new SKRect(0, 0, w, h));

        using var image = SKImage.FromBitmap(cropped);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var tilePath = Path.Combine(_dataDir, $"step_{step:D2}_tile_{row}x{col}.png");
        File.WriteAllBytes(tilePath, data.ToArray());
    }

    private static (int left, int top, int right, int bottom) FindGridBounds(SKBitmap bmp)
    {
        // Use LONGEST CONTINUOUS DARK RUN per column/row.
        // Grid border lines are continuous thick lines (300+ pixels long).
        // Factory icons and title text have short scattered dark runs.

        // For each column, find the longest continuous run of dark pixels
        var colMaxRun = new int[bmp.Width];
        for (int x = 0; x < bmp.Width; x++)
        {
            int maxRun = 0, currentRun = 0;
            for (int y = 0; y < bmp.Height; y++)
            {
                if (IsDark(bmp.GetPixel(x, y)))
                {
                    currentRun++;
                    if (currentRun > maxRun) maxRun = currentRun;
                }
                else
                {
                    currentRun = 0;
                }
            }
            colMaxRun[x] = maxRun;
        }

        // For each row, find the longest continuous run of dark pixels
        var rowMaxRun = new int[bmp.Height];
        for (int y = 0; y < bmp.Height; y++)
        {
            int maxRun = 0, currentRun = 0;
            for (int x = 0; x < bmp.Width; x++)
            {
                if (IsDark(bmp.GetPixel(x, y)))
                {
                    currentRun++;
                    if (currentRun > maxRun) maxRun = currentRun;
                }
                else
                {
                    currentRun = 0;
                }
            }
            rowMaxRun[y] = maxRun;
        }

        // Grid vertical lines span the full grid height (~300+ pixels).
        // Use threshold of 1/3 of image height to filter out icons/text.
        int vRunThreshold = bmp.Height / 3;

        int left = 0, right = bmp.Width - 1;
        for (int x = 0; x < bmp.Width; x++)
        {
            if (colMaxRun[x] >= vRunThreshold) { left = x; break; }
        }
        for (int x = bmp.Width - 1; x >= 0; x--)
        {
            if (colMaxRun[x] >= vRunThreshold) { right = x; break; }
        }

        // Grid horizontal lines span the full grid width.
        // Use threshold based on found grid width.
        int hRunThreshold = (right - left) / 3;

        int top = 0, bottom = bmp.Height - 1;
        for (int y = 0; y < bmp.Height; y++)
        {
            if (rowMaxRun[y] >= hRunThreshold) { top = y; break; }
        }
        for (int y = bmp.Height - 1; y >= 0; y--)
        {
            if (rowMaxRun[y] >= hRunThreshold) { bottom = y; break; }
        }

        // Log for debugging
        ConsoleUI.PrintInfo($"  Col longest run at left({left}): {colMaxRun[left]}, right({right}): {colMaxRun[right]}");
        ConsoleUI.PrintInfo($"  Row longest run at top({top}): {rowMaxRun[top]}, bottom({bottom}): {rowMaxRun[bottom]}");
        ConsoleUI.PrintInfo($"  Thresholds: vertical run >= {vRunThreshold}, horizontal run >= {hRunThreshold}");

        return (left, top, right, bottom);
    }

    /// <summary>
    /// Check if a cable connects to the given edge of a cell.
    /// Returns detection result, dark pixel ratio, and the sample point coordinates.
    /// </summary>
    private static (bool detected, double ratio, int sampleX, int sampleY) HasCableDetailed(
        SKBitmap bmp, int cellL, int cellT, int cellR, int cellB, CableEdge edge)
    {
        int cellW = cellR - cellL;
        int cellH = cellB - cellT;

        // Inset far enough to be past grid lines (~20% of cell dimension)
        int insetX = Math.Max(10, cellW / 5);
        int insetY = Math.Max(10, cellH / 5);

        // Sample radius — small square around the sample point
        int sampleR = Math.Max(4, Math.Min(cellW, cellH) / 15);

        int cx, cy;
        switch (edge)
        {
            case CableEdge.Top:
                cx = (cellL + cellR) / 2;
                cy = cellT + insetY;
                break;
            case CableEdge.Bottom:
                cx = (cellL + cellR) / 2;
                cy = cellB - insetY;
                break;
            case CableEdge.Left:
                cx = cellL + insetX;
                cy = (cellT + cellB) / 2;
                break;
            case CableEdge.Right:
                cx = cellR - insetX;
                cy = (cellT + cellB) / 2;
                break;
            default:
                return (false, 0, 0, 0);
        }

        int dark = 0, total = 0;
        for (int dx = -sampleR; dx <= sampleR; dx++)
        {
            for (int dy = -sampleR; dy <= sampleR; dy++)
            {
                int px = cx + dx, py = cy + dy;
                if (px >= 0 && px < bmp.Width && py >= 0 && py < bmp.Height)
                {
                    total++;
                    if (IsDark(bmp.GetPixel(px, py)))
                        dark++;
                }
            }
        }

        double ratio = total > 0 ? (double)dark / total : 0;
        return (ratio > 0.4, ratio, cx, cy);
    }

    private static bool IsDark(SKColor c)
    {
        return c.Red < DarkThreshold && c.Green < DarkThreshold && c.Blue < DarkThreshold;
    }
}
