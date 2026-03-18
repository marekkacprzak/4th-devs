using System.Diagnostics;
using FailureAgent.UI;

namespace FailureAgent.Services;

public class LogDownloader
{
    private static readonly ActivitySource Activity = new("FailureAgent.LogDownloader");

    private readonly HttpClient _http;
    private readonly string _dataBaseUrl;
    private readonly string _apiKey;

    public LogDownloader(HttpClient http, string dataBaseUrl, string apiKey)
    {
        _http = http;
        _dataBaseUrl = dataBaseUrl.TrimEnd('/');
        _apiKey = apiKey;
    }

    public async Task<string> DownloadLogFileAsync(string outputDir)
    {
        var url = $"{_dataBaseUrl}/{_apiKey}/failure.log";

        using var span = Activity.StartActivity("log.download");
        span?.SetTag("url", url);
        span?.SetTag("output.dir", outputDir);

        ConsoleUI.PrintInfo($"Downloading from: {url}");

        var content = await _http.GetStringAsync(url);
        var path = Path.Combine(outputDir, "failure.log");
        await File.WriteAllTextAsync(path, content);

        var lineCount = content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        span?.SetTag("download.chars", content.Length);
        span?.SetTag("download.lines", lineCount);
        span?.SetTag("output.path", path);

        ConsoleUI.PrintInfo($"Downloaded {content.Length} chars, {lineCount} lines");

        return path;
    }
}
