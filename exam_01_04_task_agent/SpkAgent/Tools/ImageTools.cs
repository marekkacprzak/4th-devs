using System.ClientModel;
using System.ComponentModel;
using Microsoft.Extensions.AI;
using OpenAI;
using SpkAgent.Config;
using SpkAgent.UI;

namespace SpkAgent.Tools;

public class ImageTools
{
    private readonly HttpClient _http;
    private readonly VisionConfig _visionConfig;
    private readonly string _docsDir;

    public ImageTools(HttpClient http, VisionConfig visionConfig, string docsDir)
    {
        _http = http;
        _visionConfig = visionConfig;
        _docsDir = docsDir;
    }

    [Description("Process a locally saved image file and convert its table content to CSV format. Use this for image files like trasy-wylaczone.png that contain tabular data.")]
    public async Task<string> ProcessImageToCSV(
        [Description("Filename of the image in the docs folder, e.g. 'trasy-wylaczone.png'")] string filename,
        [Description("Output CSV filename, e.g. 'trasy-wylaczone.csv'")] string outputFilename)
    {
        ConsoleUI.PrintToolCall("ProcessImageToCSV", $"filename={filename}, output={outputFilename}");

        var imagePath = Path.Combine(_docsDir, filename);
        if (!File.Exists(imagePath))
            return $"ERROR: Image file not found: {imagePath}";

        try
        {
            var imageBytes = await File.ReadAllBytesAsync(imagePath);
            var base64 = Convert.ToBase64String(imageBytes);
            var mimeType = filename.EndsWith(".png") ? "image/png" : "image/jpeg";

            var visionClient = new OpenAIClient(
                new ApiKeyCredential("lm-studio"),
                new OpenAIClientOptions { Endpoint = new Uri(_visionConfig.Endpoint) });

            var chatClient = visionClient
                .GetChatClient(_visionConfig.Model)
                .AsIChatClient();

            var messages = new List<ChatMessage>
            {
                new(ChatRole.User, [
                    new TextContent("Przekonwertuj tabel\u0119 z tego obrazu na format CSV. U\u017cyj \u015brednika (;) jako separatora. Zachowaj dok\u0142adnie polskie znaki. Zwr\u00f3\u0107 TYLKO dane CSV z nag\u0142\u00f3wkami, bez \u017cadnego dodatkowego tekstu."),
                    new DataContent(imageBytes, mimeType)
                ])
            };

            var response = await chatClient.GetResponseAsync(messages);
            var csvContent = response.Text.Trim();

            // Clean up potential markdown code block wrapping
            if (csvContent.StartsWith("```"))
            {
                var lines = csvContent.Split('\n');
                csvContent = string.Join('\n', lines.Skip(1).TakeWhile(l => !l.StartsWith("```")));
            }

            var outputPath = Path.Combine(_docsDir, outputFilename);
            await File.WriteAllTextAsync(outputPath, csvContent);

            ConsoleUI.PrintInfo($"Image converted to CSV: {outputPath}");
            return $"CSV saved to {outputFilename}:\n{csvContent}";
        }
        catch (Exception ex)
        {
            return $"ERROR processing image: {ex.Message}";
        }
    }
}
