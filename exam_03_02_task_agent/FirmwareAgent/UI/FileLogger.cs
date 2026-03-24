namespace FirmwareAgent.UI;

/// <summary>
/// Logs all API and LLM requests/responses to a timestamped file for later analysis.
/// </summary>
public class FileLogger
{
    private readonly string _logPath;
    private readonly Lock _lock = new();

    public FileLogger(string logDir)
    {
        Directory.CreateDirectory(logDir);
        _logPath = Path.Combine(logDir, $"run_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
        AppendLine($"=== FirmwareAgent Session {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
        ConsoleUI.PrintInfo($"Session log: {_logPath}");
    }

    public void LogShellRequest(string command)
    {
        AppendSection("SHELL REQUEST", command);
    }

    public void LogShellResponse(int statusCode, string body)
    {
        AppendSection($"SHELL RESPONSE ({statusCode})", body);
    }

    public void LogSubmitRequest(string confirmation)
    {
        AppendSection("SUBMIT REQUEST", confirmation);
    }

    public void LogSubmitResponse(int statusCode, string body)
    {
        AppendSection($"SUBMIT RESPONSE ({statusCode})", body);
    }

    public void LogAgentMessage(string role, string content)
    {
        AppendSection($"AGENT [{role.ToUpper()}]", content);
    }

    public void LogError(string context, string error)
    {
        AppendSection($"ERROR [{context}]", error);
    }

    private void AppendSection(string label, string content)
    {
        var entry = $"--- [{label}] @ {DateTime.Now:HH:mm:ss} ---\n{content}\n\n";
        AppendLine(entry);
    }

    private void AppendLine(string text)
    {
        lock (_lock)
        {
            File.AppendAllText(_logPath, text);
        }
    }
}
