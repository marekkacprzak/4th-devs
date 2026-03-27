namespace SavethemAgent.Services;

public class RequestLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();

    public string LogFilePath { get; }

    public RequestLogger(string logsDirectory)
    {
        Directory.CreateDirectory(logsDirectory);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        LogFilePath = Path.Combine(logsDirectory, $"savethem_run_{timestamp}.log");
        _writer = new StreamWriter(LogFilePath, append: false, System.Text.Encoding.UTF8) { AutoFlush = true };
        _writer.WriteLine($"# SavethemAgent HTTP Log — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _writer.WriteLine();
    }

    public void LogRequest(string method, string url, string body)
    {
        lock (_lock)
        {
            _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] >>> {method} {url}");
            _writer.WriteLine(body);
            _writer.WriteLine();
        }
    }

    public void LogResponse(int statusCode, string body)
    {
        lock (_lock)
        {
            _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] <<< {statusCode}");
            _writer.WriteLine(body);
            _writer.WriteLine("---");
            _writer.WriteLine();
        }
    }

    public void LogComment(string comment)
    {
        lock (_lock)
        {
            _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] # {comment}");
            _writer.WriteLine();
        }
    }

    public void Dispose() => _writer.Dispose();
}
