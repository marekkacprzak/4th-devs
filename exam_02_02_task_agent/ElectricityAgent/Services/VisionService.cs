using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using SkiaSharp;
using ElectricityAgent.Models;
using ElectricityAgent.UI;

namespace ElectricityAgent.Services;

public class VisionService
{
    private readonly IChatClient _visionClient;

    public VisionService(IChatClient visionClient)
    {
        _visionClient = visionClient;
    }

    public async Task<BoardState> InterpretBoardAsync(byte[] pngBytes)
    {
        var board = new BoardState();
        var tileImages = CropTiles(pngBytes);

        for (int row = 1; row <= 3; row++)
        {
            for (int col = 1; col <= 3; col++)
            {
                var index = (row - 1) * 3 + (col - 1);
                var tileImage = tileImages[index];
                ConsoleUI.PrintInfo($"Interpreting tile {row}x{col}...");

                var tile = await InterpretTileAsync(tileImage, row, col);
                board.SetTile(row, col, tile);
                ConsoleUI.PrintInfo($"  {tile}");
            }
        }

        return board;
    }

    private async Task<GridTile> InterpretTileAsync(byte[] tileImageBytes, int row, int col)
    {
        var prompt = """
            Look at this electrical cable tile image from a puzzle grid.
            Determine which edges of this tile have cable/wire connections.
            A cable connection on an edge means a cable/wire visually touches or extends to that edge of the tile.

            Also check if there is any text label visible on the tile (like "PWR6132PL" or similar power plant code, or "SOURCE").

            Respond with ONLY a JSON object in this exact format, no other text:
            {"top": true, "right": false, "bottom": true, "left": false, "label": null}

            Set each direction to true if a cable connects to that edge, false otherwise.
            Set "label" to the text shown on the tile or null if no label is visible.
            """;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, [
                new DataContent(tileImageBytes, "image/png"),
                new TextContent(prompt)
            ])
        };

        var response = await _visionClient.GetResponseAsync(messages);
        var responseText = response.Text ?? "";

        // Strip thinking tokens (qwen models may include <think>...</think>)
        responseText = StripThinkingTokens(responseText);

        return ParseTileResponse(responseText, row, col);
    }

    private static GridTile ParseTileResponse(string responseText, int row, int col)
    {
        var tile = new GridTile { Row = row, Column = col };

        try
        {
            // Try to extract JSON from the response (it may have surrounding text)
            var jsonMatch = Regex.Match(responseText, @"\{[^}]*\}", RegexOptions.Singleline);
            if (!jsonMatch.Success)
            {
                ConsoleUI.PrintError($"No JSON found in vision response for {row}x{col}: {responseText}");
                return tile;
            }

            using var doc = JsonDocument.Parse(jsonMatch.Value);
            var root = doc.RootElement;

            var connections = CableEdge.None;
            if (GetBool(root, "top")) connections |= CableEdge.Top;
            if (GetBool(root, "right")) connections |= CableEdge.Right;
            if (GetBool(root, "bottom")) connections |= CableEdge.Bottom;
            if (GetBool(root, "left")) connections |= CableEdge.Left;

            tile.Connections = connections;

            if (root.TryGetProperty("label", out var labelProp) &&
                labelProp.ValueKind == JsonValueKind.String)
            {
                var label = labelProp.GetString();
                if (!string.IsNullOrWhiteSpace(label) &&
                    !label.Equals("null", StringComparison.OrdinalIgnoreCase))
                {
                    tile.Label = label;
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleUI.PrintError($"Failed to parse vision response for {row}x{col}: {ex.Message}");
        }

        return tile;
    }

    private static bool GetBool(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
            if (prop.ValueKind == JsonValueKind.String)
                return prop.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
        }
        return false;
    }

    private static List<byte[]> CropTiles(byte[] fullBoardPng)
    {
        var tiles = new List<byte[]>();

        using var bitmap = SKBitmap.Decode(fullBoardPng);
        if (bitmap == null)
            throw new InvalidOperationException("Failed to decode board PNG image");

        var tileWidth = bitmap.Width / 3;
        var tileHeight = bitmap.Height / 3;

        // Add small inward padding to avoid grid lines
        var padX = Math.Max(2, tileWidth / 20);
        var padY = Math.Max(2, tileHeight / 20);

        ConsoleUI.PrintInfo($"Board image: {bitmap.Width}x{bitmap.Height}, tile size: {tileWidth}x{tileHeight}");

        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                var cropRect = new SKRectI(
                    col * tileWidth + padX,
                    row * tileHeight + padY,
                    (col + 1) * tileWidth - padX,
                    (row + 1) * tileHeight - padY);

                using var cropped = new SKBitmap(cropRect.Width, cropRect.Height);
                using var canvas = new SKCanvas(cropped);
                canvas.DrawBitmap(bitmap, cropRect, new SKRect(0, 0, cropRect.Width, cropRect.Height));

                using var image = SKImage.FromBitmap(cropped);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                tiles.Add(data.ToArray());
            }
        }

        return tiles;
    }

    private static string StripThinkingTokens(string text)
    {
        // Remove <think>...</think> blocks from qwen model responses
        return Regex.Replace(text, @"<think>.*?</think>", "", RegexOptions.Singleline).Trim();
    }
}
