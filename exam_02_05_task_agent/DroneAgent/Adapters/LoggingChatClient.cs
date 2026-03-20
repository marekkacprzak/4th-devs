using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using DroneAgent.UI;

namespace DroneAgent.Adapters;

public class LoggingChatClient : DelegatingChatClient
{
    private static readonly ActivitySource Activity = new("DroneAgent.AI");

    private readonly string _label;
    private readonly string? _logFilePath;

    public LoggingChatClient(IChatClient innerClient, string label, string? logFilePath = null)
        : base(innerClient)
    {
        _label = label;
        _logFilePath = logFilePath;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var span = Activity.StartActivity($"ai.chat.{_label}");

        var messagesList = messages.ToList();
        var requestSummary = FormatMessages(messagesList);

        span?.SetTag("ai.label", _label);
        span?.SetTag("ai.request.message_count", messagesList.Count);
        span?.SetTag("ai.request.content", requestSummary);

        if (options?.Tools is { Count: > 0 })
        {
            span?.SetTag("ai.request.tools", string.Join(", ",
                options.Tools.Select(t => t is AIFunction f ? f.Name : t.GetType().Name)));
        }

        LogToFile($"REQUEST [{_label}]", requestSummary);
        ConsoleUI.PrintInfo($"[AI {_label}] Sending {messagesList.Count} message(s)...");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await base.GetResponseAsync(messagesList, options, cancellationToken);
            stopwatch.Stop();

            var responseText = response.Text ?? "";
            var responseSummary = FormatResponse(response);

            span?.SetTag("ai.response.content", responseSummary);
            span?.SetTag("ai.response.duration_ms", stopwatch.ElapsedMilliseconds);
            span?.SetTag("ai.response.finish_reason", response.FinishReason?.ToString());

            if (response.Usage is { } usage)
            {
                span?.SetTag("ai.response.input_tokens", usage.InputTokenCount);
                span?.SetTag("ai.response.output_tokens", usage.OutputTokenCount);
                span?.SetTag("ai.response.total_tokens", usage.TotalTokenCount);
            }

            LogToFile($"RESPONSE [{_label}] ({stopwatch.ElapsedMilliseconds}ms)", responseSummary);
            ConsoleUI.PrintInfo($"[AI {_label}] Response received in {stopwatch.ElapsedMilliseconds}ms ({responseText.Length} chars)");

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            span?.SetStatus(ActivityStatusCode.Error, ex.Message);
            span?.SetTag("ai.error", ex.Message);
            LogToFile($"ERROR [{_label}] ({stopwatch.ElapsedMilliseconds}ms)", ex.Message);
            throw;
        }
    }

    private static string FormatMessages(List<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        foreach (var msg in messages)
        {
            sb.AppendLine($"[{msg.Role}]");
            foreach (var content in msg.Contents)
            {
                switch (content)
                {
                    case TextContent text:
                        sb.AppendLine(text.Text);
                        break;
                    case FunctionCallContent fc:
                        sb.AppendLine($"  >> tool_call: {fc.Name}({JsonSerializer.Serialize(fc.Arguments)})");
                        break;
                    case FunctionResultContent fr:
                        sb.AppendLine($"  << tool_result [{fr.CallId}]: {fr.Result}");
                        break;
                    case DataContent dc:
                        sb.AppendLine($"  [data: {dc.MediaType}, {dc.Data.Length} bytes]");
                        break;
                    default:
                        sb.AppendLine($"  [{content.GetType().Name}]");
                        break;
                }
            }
        }
        return sb.ToString();
    }

    private static string FormatResponse(ChatResponse response)
    {
        var sb = new StringBuilder();

        foreach (var msg in response.Messages)
        {
            sb.AppendLine($"[{msg.Role}]");
            foreach (var content in msg.Contents)
            {
                switch (content)
                {
                    case TextContent text:
                        sb.AppendLine(text.Text);
                        break;
                    case FunctionCallContent fc:
                        sb.AppendLine($"  >> tool_call: {fc.Name}({JsonSerializer.Serialize(fc.Arguments)})");
                        break;
                    default:
                        sb.AppendLine($"  [{content.GetType().Name}]");
                        break;
                }
            }
        }

        if (response.Usage is { } usage)
            sb.AppendLine($"[tokens: in={usage.InputTokenCount}, out={usage.OutputTokenCount}, total={usage.TotalTokenCount}]");

        return sb.ToString();
    }

    private void LogToFile(string header, string content)
    {
        if (_logFilePath is null) return;

        try
        {
            var entry = $"--- {header} @ {DateTime.Now:HH:mm:ss} ---\n{content}\n\n";
            File.AppendAllText(_logFilePath, entry);
        }
        catch
        {
            // Don't fail on logging errors
        }
    }
}
