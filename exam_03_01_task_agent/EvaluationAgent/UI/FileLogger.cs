namespace EvaluationAgent.UI;

public static class FileLogger
{
    private static string? _path;

    public static void Initialize(string path)
    {
        _path = path;
        File.WriteAllText(path, $"=== EvaluationAgent Log Session {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n\n");
    }

    public static void Log(string label, string content)
    {
        if (_path == null) return;
        var entry = $"--- [{label}] @ {DateTime.Now:HH:mm:ss} ---\n{content}\n\n";
        File.AppendAllText(_path, entry);
    }
}
