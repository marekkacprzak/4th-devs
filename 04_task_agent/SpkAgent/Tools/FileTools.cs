using System.ComponentModel;
using SpkAgent.UI;

namespace SpkAgent.Tools;

public class FileTools
{
    private readonly string _basePath;

    public FileTools(string basePath)
    {
        _basePath = basePath;
    }

    [Description("Read contents of a local file (CSV, JSON, TXT, MD). Use relative paths like 'docs/index.md'.")]
    public string ReadFile(
        [Description("File path relative to project root")] string path)
    {
        ConsoleUI.PrintToolCall("ReadFile", $"path={path}");

        var fullPath = Path.GetFullPath(Path.Combine(_basePath, path));

        if (!fullPath.StartsWith(_basePath))
            return "ERROR: Access denied - path outside allowed directory.";

        if (!File.Exists(fullPath))
            return $"ERROR: File not found: {path}";

        return File.ReadAllText(fullPath);
    }
}
