using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace MailboxAgent.Services;

/// <summary>
/// IChatClient middleware that logs every LLM request/response to a file.
/// Captures all rounds of the agent loop including tool calls.
/// </summary>
public class FileLoggingChatClient : DelegatingChatClient
{
    private readonly string _logFilePath;
    private int _callCounter;
    private static readonly object Lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public FileLoggingChatClient(IChatClient inner, string logFilePath) : base(inner)
    {
        _logFilePath = logFilePath;

        // Initialize log file
        var dir = Path.GetDirectoryName(logFilePath);
        if (dir != null) Directory.CreateDirectory(dir);
        File.WriteAllText(logFilePath, $"=== AI Log Session {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n\n");
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var callId = Interlocked.Increment(ref _callCounter);
        var timestamp = DateTime.Now;

        // Log request
        var requestLog = new StringBuilder();
        requestLog.AppendLine($"{"",60}");
        requestLog.AppendLine($"======== CALL #{callId} @ {timestamp:HH:mm:ss.fff} ========");
        requestLog.AppendLine();

        foreach (var msg in messages)
        {
            requestLog.AppendLine($"--- [{msg.Role}] ---");

            foreach (var content in msg.Contents)
            {
                switch (content)
                {
                    case TextContent text:
                        requestLog.AppendLine(text.Text);
                        break;

                    case FunctionCallContent funcCall:
                        requestLog.AppendLine($"[TOOL CALL] {funcCall.Name}({FormatArguments(funcCall.Arguments)})");
                        break;

                    case FunctionResultContent funcResult:
                        var resultText = funcResult.Result?.ToString() ?? "(null)";
                        // Truncate very long tool results for readability
                        if (resultText.Length > 2000)
                            resultText = resultText[..2000] + $"\n... [truncated, total {resultText.Length} chars]";
                        requestLog.AppendLine($"[TOOL RESULT] {funcResult.CallId}: {resultText}");
                        break;

                    default:
                        requestLog.AppendLine($"[{content.GetType().Name}]");
                        break;
                }
            }
            requestLog.AppendLine();
        }

        if (options?.Tools?.Count > 0)
        {
            requestLog.AppendLine($"[AVAILABLE TOOLS: {string.Join(", ", options.Tools.Select(t => t is AIFunction f ? f.Name : t.GetType().Name))}]");
            requestLog.AppendLine();
        }

        AppendToLog(requestLog.ToString());

        // Execute
        var response = await base.GetResponseAsync(messages, options, cancellationToken);

        // Log response
        var responseLog = new StringBuilder();
        responseLog.AppendLine($"-------- RESPONSE #{callId} @ {DateTime.Now:HH:mm:ss.fff} (took {(DateTime.Now - timestamp).TotalSeconds:F1}s) --------");
        responseLog.AppendLine();

        foreach (var msg in response.Messages)
        {
            responseLog.AppendLine($"--- [{msg.Role}] ---");

            foreach (var content in msg.Contents)
            {
                switch (content)
                {
                    case TextContent text:
                        responseLog.AppendLine(text.Text);
                        break;

                    case FunctionCallContent funcCall:
                        responseLog.AppendLine($"[TOOL CALL] {funcCall.Name}({FormatArguments(funcCall.Arguments)})");
                        break;

                    case FunctionResultContent funcResult:
                        var resultText = funcResult.Result?.ToString() ?? "(null)";
                        if (resultText.Length > 2000)
                            resultText = resultText[..2000] + $"\n... [truncated, total {resultText.Length} chars]";
                        responseLog.AppendLine($"[TOOL RESULT] {funcResult.CallId}: {resultText}");
                        break;

                    default:
                        responseLog.AppendLine($"[{content.GetType().Name}]");
                        break;
                }
            }
        }

        responseLog.AppendLine();
        responseLog.AppendLine($"[FINISH REASON: {response.FinishReason}]");
        responseLog.AppendLine();

        AppendToLog(responseLog.ToString());

        return response;
    }

    private static string FormatArguments(IDictionary<string, object?>? args)
    {
        if (args == null || args.Count == 0) return "";
        return string.Join(", ", args.Select(kv =>
        {
            var val = kv.Value?.ToString() ?? "null";
            if (val.Length > 200) val = val[..200] + "...";
            return $"{kv.Key}={val}";
        }));
    }

    private void AppendToLog(string text)
    {
        lock (Lock)
        {
            File.AppendAllText(_logFilePath, text);
        }
    }
}
