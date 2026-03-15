using System.ComponentModel;

namespace RailwayAgent.Tools;

public class FileTools
{
    private readonly string _basePath;

    public FileTools(string basePath)
    {
        _basePath = basePath;
    }

    [Description("Read contents of a local file (CSV, JSON, TXT). Use relative paths like 'trasy_wylaczone.csv' or 'help_answer.json'.")]
    public string ReadFile(
        [Description("File path relative to project root, e.g. 'trasy_wylaczone.csv'")] string path)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[Tool] ReadFile(path={path})");
        Console.ResetColor();

        var fullPath = Path.GetFullPath(Path.Combine(_basePath, path));

        if (!fullPath.StartsWith(_basePath))
            return "ERROR: Access denied - path outside allowed directory.";

        if (!File.Exists(fullPath))
            return $"ERROR: File not found: {path}";

        return File.ReadAllText(fullPath);
    }
}
