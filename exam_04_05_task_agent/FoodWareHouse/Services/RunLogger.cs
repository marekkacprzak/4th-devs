using System.Text;

namespace FoodWareHouse.Services;

/// <summary>
/// Writes a structured, timestamped log file for the entire agent run.
/// Every request, response and phase transition is written in full — nothing is truncated.
/// File location: logs/YYYY-MM-DD_HH-mm-ss.log (relative to the working directory).
/// </summary>
public sealed class RunLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly Lock _lock = new();

    public string FilePath { get; }

    public RunLogger(string logDir = "logs")
    {
        Directory.CreateDirectory(logDir);
        FilePath = Path.Combine(logDir, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
        _writer = new StreamWriter(FilePath, append: false, Encoding.UTF8) { AutoFlush = true };
        Write("SESSION_START", $"Log started: {DateTime.Now:O}");
    }

    public void LogLlmRequest(int iteration, int maxIterations, int messageCount)
        => Write("LLM_REQUEST", $"iteration={iteration}/{maxIterations}  messages_in_context={messageCount}");

    public void LogLlmResponse(string rawText)
        => Write("LLM_RESPONSE", rawText);

    public void LogToolCall(string name, string callId, string? argsJson)
        => Write("TOOL_CALL", $"name={name}  callId={callId}\n{argsJson ?? "(no args)"}");

    public void LogToolResult(string callId, string result, bool isError)
        => Write(isError ? "TOOL_ERROR" : "TOOL_RESULT", $"callId={callId}\n{result}");

    public void LogApiRequest(string url, string body)
        => Write("API_REQUEST", $"POST {url}\n{body}");

    public void LogApiResponse(int statusCode, string body)
        => Write(statusCode >= 200 && statusCode < 300 ? "API_RESPONSE_OK" : "API_RESPONSE_ERROR",
                 $"status={statusCode}\n{body}");

    public void LogNetworkError(string url, string error)
        => Write("NETWORK_ERROR", $"url={url}\n{error}");

    public void LogError(string context, string message)
        => Write("ERROR", $"context={context}\n{message}");

    public void LogInfo(string message)
        => Write("INFO", message);

    public void LogPhase(int phase, string name)
        => Write("PHASE", $"Phase {phase}: {name}");

    public void LogDbQuery(string query, string result)
        => Write("DB_QUERY", $"query={query}\n{result}");

    public void LogOrderCreated(string city, string orderId)
        => Write("ORDER_CREATED", $"city={city}  orderId={orderId}");

    public void LogOrderItems(string orderId, string itemsJson)
        => Write("ORDER_ITEMS", $"orderId={orderId}\n{itemsJson}");

    public void LogSignature(string city, string signature)
        => Write("SIGNATURE", $"city={city}  signature={signature}");

    private void Write(string tag, string content)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        lock (_lock)
        {
            _writer.WriteLine($"[{timestamp}] [{tag}]");
            _writer.WriteLine(content);
            _writer.WriteLine(new string('─', 80));
        }
    }

    public void Dispose() => _writer.Dispose();
}
