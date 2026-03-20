using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using DroneAgent.UI;

namespace DroneAgent.Tools;

public class MapAnalysisTool
{
    private static readonly ActivitySource Activity = new("DroneAgent.Tools");

    private readonly IChatClient _visionClient;
    private readonly string _mapUrl;
    private readonly HttpClient _httpClient;

    public MapAnalysisTool(IChatClient visionClient, string mapUrl, HttpClient httpClient)
    {
        _visionClient = visionClient;
        _mapUrl = mapUrl;
        _httpClient = httpClient;
    }

    [Description("Analyze the drone map image to locate the dam sector. Returns the dam's grid position (column and row, 1-indexed).")]
    public async Task<string> AnalyzeMap()
    {
        using var span = Activity.StartActivity("tool.AnalyzeMap");
        span?.SetTag("tool.name", "AnalyzeMap");
        span?.SetTag("tool.map_url", _mapUrl);

        ConsoleUI.PrintToolCall("AnalyzeMap", _mapUrl);

        try
        {
            // Download image and send as base64 (more reliable with local models)
            var imageBytes = await _httpClient.GetByteArrayAsync(_mapUrl);
            ConsoleUI.PrintInfo($"Downloaded map image: {imageBytes.Length} bytes");

            var messages = new List<ChatMessage>
            {
                new(ChatRole.User, [
                    new TextContent("""
                        /no_think
                        This aerial photo shows the Zarnowiec power plant area with a red grid overlay.
                        The grid has 2 inner vertical lines creating 3 COLUMNS and 3 inner horizontal lines creating 4 ROWS.
                        So valid coordinates range from (1,1) to (3,4).
                        Top-left corner is (1,1). Column counts left-to-right, row counts top-to-bottom.

                        Find the sector containing the DAM (tama). The dam holds back water from a lake.
                        The water near the dam has INTENSIFIED BLUE color to help you find it.
                        Look for blue/turquoise water that is more vivid than other water on the map.

                        Which sector (column,row) contains the most intensified blue water?

                        Reply ONLY: column=X, row=Y
                        """),
                    new DataContent(imageBytes, "image/png")
                ])
            };

            var response = await _visionClient.GetResponseAsync(messages);
            var result = response.Text?.Trim() ?? "No response from vision model";

            span?.SetTag("tool.result", result);
            ConsoleUI.PrintInfo($"Vision model result: {result}");
            return result;
        }
        catch (Exception ex)
        {
            span?.SetStatus(ActivityStatusCode.Error, ex.Message);
            span?.SetTag("tool.error", ex.Message);
            var error = $"Error analyzing map: {ex.Message}";
            ConsoleUI.PrintError(error);
            return error;
        }
    }
}
