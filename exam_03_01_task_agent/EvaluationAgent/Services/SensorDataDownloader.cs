using System.Diagnostics;
using System.IO.Compression;
using EvaluationAgent.UI;

namespace EvaluationAgent.Services;

public class SensorDataDownloader
{
    private static readonly ActivitySource Activity = new("EvaluationAgent.SensorDownloader");

    private readonly HttpClient _http;
    private readonly string _sensorsZipUrl;

    public SensorDataDownloader(HttpClient http, string sensorsZipUrl)
    {
        _http = http;
        _sensorsZipUrl = sensorsZipUrl;
    }

    public async Task<string> DownloadAndExtractAsync(string dataDir)
    {
        using var span = Activity.StartActivity("sensor.download_and_extract");

        var sensorsDir = Path.Combine(dataDir, "sensors");
        var zipPath = Path.Combine(dataDir, "sensors.zip");

        // Check cache: if sensors dir already has JSON files, skip download
        if (Directory.Exists(sensorsDir))
        {
            var existing = Directory.GetFiles(sensorsDir, "*.json");
            if (existing.Length > 0)
            {
                ConsoleUI.PrintInfo($"Using cached sensor data: {existing.Length} files in {sensorsDir}");
                span?.SetTag("cache.hit", true);
                span?.SetTag("files.count", existing.Length);
                return sensorsDir;
            }
        }

        Directory.CreateDirectory(sensorsDir);
        span?.SetTag("cache.hit", false);

        // Download ZIP
        ConsoleUI.PrintInfo($"Downloading sensors.zip from {_sensorsZipUrl}...");
        span?.SetTag("download.url", _sensorsZipUrl);

        using var response = await _http.GetAsync(_sensorsZipUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        ConsoleUI.PrintInfo($"Download size: {contentLength?.ToString() ?? "unknown"} bytes");

        await using (var stream = await response.Content.ReadAsStreamAsync())
        await using (var fileStream = File.Create(zipPath))
        {
            await stream.CopyToAsync(fileStream);
        }

        ConsoleUI.PrintInfo($"Downloaded to {zipPath}, extracting...");

        // Extract ZIP
        ZipFile.ExtractToDirectory(zipPath, sensorsDir, overwriteFiles: true);

        var files = Directory.GetFiles(sensorsDir, "*.json");
        ConsoleUI.PrintInfo($"Extracted {files.Length} JSON files to {sensorsDir}");
        span?.SetTag("files.count", files.Length);

        // Clean up ZIP file to save space
        File.Delete(zipPath);

        return sensorsDir;
    }
}
