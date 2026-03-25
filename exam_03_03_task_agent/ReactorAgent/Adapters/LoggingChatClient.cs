using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ReactorAgent.Adapters;

/// <summary>
/// A delegating IChatClient that logs every LLM request and response to a file.
/// Wraps an inner client and forwards all calls to it, intercepting before/after.
/// </summary>
public class LoggingChatClient : DelegatingChatClient
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();
    private int _stepCounter;

    public string LogFilePath { get; }

    public LoggingChatClient(IChatClient innerClient, string logsDirectory)
        : base(innerClient)
    {
        Directory.CreateDirectory(logsDirectory);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        LogFilePath = Path.Combine(logsDirectory, $"reactor_agent_{timestamp}.log");
        _writer = new StreamWriter(LogFilePath, append: false, Encoding.UTF8) { AutoFlush = true };
        _writer.WriteLine($"# ReactorAgent LLM Log — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _writer.WriteLine();
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var step = Interlocked.Increment(ref _stepCounter);
        var messageList = chatMessages.ToList();
        LogMessages(step, messageList, options);

        var result = await base.GetResponseAsync(chatMessages, options, cancellationToken);

        LogResponse(step, result);
        return result;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var step = Interlocked.Increment(ref _stepCounter);
        var messageList = chatMessages.ToList();
        LogMessages(step, messageList, options);

        await foreach (var update in base.GetStreamingResponseAsync(chatMessages, options, cancellationToken))
        {
            yield return update;
        }
    }

    private void LogMessages(int step, IList<ChatMessage> messages, ChatOptions? options)
    {
        lock (_lock)
        {
            _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] === STEP {step} REQUEST ===");
            if (options?.Tools?.Count > 0)
            {
                _writer.WriteLine($"Tools: {string.Join(", ", options.Tools.Select(t => t is AIFunction f ? f.Name : t.ToString()))}");
            }
            foreach (var msg in messages)
            {
                _writer.WriteLine($"[{msg.Role}]");
                foreach (var part in msg.Contents)
                {
                    if (part is TextContent text)
                        _writer.WriteLine(text.Text);
                    else if (part is FunctionCallContent fc)
                        _writer.WriteLine($"TOOL_CALL: {fc.Name}({FormatArgs(fc.Arguments)})");
                    else if (part is FunctionResultContent fr)
                        _writer.WriteLine($"TOOL_RESULT [{fr.CallId}]: {fr.Result}");
                    else
                        _writer.WriteLine(part.ToString());
                }
            }
            _writer.WriteLine();
        }
    }

    private void LogResponse(int step, ChatResponse result)
    {
        lock (_lock)
        {
            _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] === STEP {step} RESPONSE ===");
            _writer.WriteLine($"FinishReason: {result.FinishReason}");
            foreach (var msg in result.Messages)
            {
                _writer.WriteLine($"[{msg.Role}]");
                foreach (var part in msg.Contents)
                {
                    if (part is TextContent text)
                        _writer.WriteLine(text.Text);
                    else if (part is FunctionCallContent fc)
                        _writer.WriteLine($"TOOL_CALL: {fc.Name}({FormatArgs(fc.Arguments)})");
                    else
                        _writer.WriteLine(part.ToString());
                }
            }
            _writer.WriteLine("---");
            _writer.WriteLine();
        }
    }

    private static string FormatArgs(IDictionary<string, object?>? args)
    {
        if (args == null || args.Count == 0) return "";
        return string.Join(", ", args.Select(kv => $"{kv.Key}={JsonSerializer.Serialize(kv.Value)}"));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _writer.Dispose();
        base.Dispose(disposing);
    }
}
