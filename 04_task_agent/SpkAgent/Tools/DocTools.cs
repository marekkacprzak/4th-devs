using System.ComponentModel;
using System.Text.RegularExpressions;
using SpkAgent.UI;

namespace SpkAgent.Tools;

public class DocTools
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _docsDir;
    private bool _allDocsDownloaded;

    public DocTools(HttpClient http, string baseUrl, string docsDir)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _docsDir = docsDir;
        Directory.CreateDirectory(_docsDir);
    }

    [Description("Download a single document from the SPK documentation server. Returns the text content.")]
    public async Task<string> DownloadDocument(
        [Description("Filename to download, e.g. 'index.md' or 'zalacznik-E.md'")] string filename)
    {
        ConsoleUI.PrintToolCall("DownloadDocument", $"filename={filename}");

        // Check if already downloaded locally
        var localPath = Path.Combine(_docsDir, filename);
        if (File.Exists(localPath) && !filename.EndsWith(".png") && !filename.EndsWith(".jpg"))
        {
            var cached = await File.ReadAllTextAsync(localPath);
            ConsoleUI.PrintInfo($"Already downloaded {filename}, returning cached ({cached.Length} chars)");
            return cached;
        }

        try
        {
            var url = $"{_baseUrl}/{filename}";
            var content = await _http.GetStringAsync(url);
            await File.WriteAllTextAsync(localPath, content);
            ConsoleUI.PrintInfo($"Downloaded {filename} ({content.Length} chars)");
            return content;
        }
        catch (Exception ex)
        {
            return $"ERROR downloading {filename}: {ex.Message}";
        }
    }

    [Description("Download index.md and ALL referenced files at once. Call this ONCE at the start. Returns summary of downloaded files. After this, use ReadFile to read individual files from 'docs/' folder.")]
    public async Task<string> DownloadAllDocs()
    {
        ConsoleUI.PrintToolCall("DownloadAllDocs");

        // Guard: only download once
        if (_allDocsDownloaded)
        {
            var existing = Directory.GetFiles(_docsDir).Select(Path.GetFileName);
            return $"ALREADY DOWNLOADED. Files available in docs/ folder: {string.Join(", ", existing)}. Use ReadFile('docs/filename') to read them. Do NOT call DownloadAllDocs again.";
        }

        try
        {
            var indexUrl = $"{_baseUrl}/index.md";
            var indexContent = await _http.GetStringAsync(indexUrl);
            await File.WriteAllTextAsync(Path.Combine(_docsDir, "index.md"), indexContent);

            var files = new List<string> { "index.md" };

            var includeMatches = Regex.Matches(indexContent, @"include\s+file=""([^""]+)""", RegexOptions.IgnoreCase);
            foreach (Match m in includeMatches)
                files.Add(m.Groups[1].Value);

            var attachmentMatches = Regex.Matches(indexContent, @"(zalacznik-[A-Z]\.md|dodatkowe-wagony\.md|trasy-wylaczone\.\w+)", RegexOptions.IgnoreCase);
            foreach (Match m in attachmentMatches)
            {
                if (!files.Contains(m.Groups[1].Value))
                    files.Add(m.Groups[1].Value);
            }

            var fileRefMatches = Regex.Matches(indexContent, @"\b([\w-]+\.(md|csv|png|jpg))\b", RegexOptions.IgnoreCase);
            foreach (Match m in fileRefMatches)
            {
                var fname = m.Groups[1].Value;
                if (!files.Contains(fname) && fname != "index.md")
                    files.Add(fname);
            }

            var results = new List<string>();
            foreach (var file in files.Distinct())
            {
                try
                {
                    var url = $"{_baseUrl}/{file}";
                    if (file.EndsWith(".png") || file.EndsWith(".jpg"))
                    {
                        var bytes = await _http.GetByteArrayAsync(url);
                        await File.WriteAllBytesAsync(Path.Combine(_docsDir, file), bytes);
                        results.Add($"{file} (image, {bytes.Length} bytes)");
                    }
                    else
                    {
                        var content = await _http.GetStringAsync(url);
                        await File.WriteAllTextAsync(Path.Combine(_docsDir, file), content);
                        results.Add($"{file} ({content.Length} chars)");
                    }
                }
                catch (Exception ex)
                {
                    results.Add($"{file} (ERROR: {ex.Message})");
                }
            }

            _allDocsDownloaded = true;

            var summary = $"All {results.Count} files downloaded to docs/ folder:\n" +
                string.Join("\n", results.Select(r => $"  - {r}")) +
                "\n\nNEXT STEP: Use ReadFile('docs/filename') to read individual files. Do NOT call DownloadAllDocs again.";

            ConsoleUI.PrintInfo(summary);
            return summary;
        }
        catch (Exception ex)
        {
            return $"ERROR downloading docs: {ex.Message}";
        }
    }
}
