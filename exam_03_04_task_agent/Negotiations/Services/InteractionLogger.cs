namespace Negotiations.Services;

public class InteractionLogger
{
    private readonly string _logFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public InteractionLogger(string logDirectory = "logs")
    {
        Directory.CreateDirectory(logDirectory);
        _logFilePath = Path.Combine(logDirectory, $"interactions_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        var header = $"=== Negotiations Interaction Log — {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}";
        File.WriteAllText(_logFilePath, header);
    }

    public string LogFilePath => _logFilePath;

    public async Task LogApiInteraction(string direction, string endpoint, string body)
    {
        var entry = $"""
            [{DateTime.Now:HH:mm:ss.fff}] API {direction} [{endpoint}]
            {body}
            ---
            """;
        await WriteAsync(entry);
    }

    public async Task LogLlmInteraction(string role, string content)
    {
        var entry = $"""
            [{DateTime.Now:HH:mm:ss.fff}] LLM [{role}]
            {content}
            ---
            """;
        await WriteAsync(entry);
    }

    public async Task LogInfo(string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss.fff}] INFO {message}{Environment.NewLine}";
        await WriteAsync(entry);
    }

    private async Task WriteAsync(string content)
    {
        await _lock.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(_logFilePath, content + Environment.NewLine);
        }
        finally
        {
            _lock.Release();
        }
    }
}
