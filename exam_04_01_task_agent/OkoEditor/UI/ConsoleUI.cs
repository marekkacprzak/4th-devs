using Spectre.Console;

namespace OkoEditor.UI;

public static class ConsoleUI
{
    public static void PrintBanner(string title, string? subtitle = null)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new FigletText(title)
            .Color(Color.Cyan1)
            .Centered());

        if (subtitle is not null)
        {
            AnsiConsole.Write(new Rule($"[grey]{Markup.Escape(subtitle)}[/]")
                .RuleStyle("dim"));
        }

        AnsiConsole.WriteLine();
    }

    public static void PrintIncomingRequest(string sessionId, string msg)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[bold green]Incoming Request[/] [dim]session={Markup.Escape(sessionId)}[/]")
            .LeftJustified().RuleStyle("green"));
        AnsiConsole.MarkupLine($"  [cyan]{Markup.Escape(Truncate(msg, 500))}[/]");
    }

    public static void PrintToolCall(string name, string? parameters = null)
    {
        var label = parameters is not null
            ? $"[bold yellow]{Markup.Escape(name)}[/]([cyan]{Markup.Escape(parameters)}[/])"
            : $"[bold yellow]{Markup.Escape(name)}[/]()";

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[bold]>> Tool Call[/]").LeftJustified().RuleStyle("yellow"));
        AnsiConsole.Write(new Panel(label)
            .Header("[yellow]>[/]")
            .BorderColor(Color.Yellow)
            .RoundedBorder()
            .Padding(1, 0));
    }

    /// <summary>
    /// Logs a tool call with its full arguments and call ID for debugging.
    /// </summary>
    public static void PrintToolCallDetail(string name, string callId, string? argsJson)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[bold yellow]>> Tool: {Markup.Escape(name)}[/] [dim]{Markup.Escape(callId)}[/]")
            .LeftJustified().RuleStyle("yellow"));
        if (!string.IsNullOrEmpty(argsJson))
            AnsiConsole.MarkupLine($"  [yellow]Args:[/] [dim]{Markup.Escape(argsJson)}[/]");
    }

    /// <summary>
    /// Logs a tool result. Shows full body — no truncation — so errors are fully visible.
    /// </summary>
    public static void PrintToolResult(string callId, string result, bool isError = false)
    {
        var color = isError ? "red" : "green";
        var header = isError ? "ERROR" : "Result";
        AnsiConsole.Write(new Panel($"[{color}]{Markup.Escape(result)}[/]")
            .Header($"[{color}]{Markup.Escape(header)} ({Markup.Escape(callId)})[/]")
            .BorderColor(isError ? Color.Red : Color.Green)
            .RoundedBorder()
            .Padding(1, 0));
    }

    /// <summary>
    /// Logs the outgoing LLM request: iteration number and total message count.
    /// </summary>
    public static void PrintLlmRequest(int iteration, int maxIterations, int messageCount)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule(
            $"[bold blue]LLM Call {iteration}/{maxIterations}[/] [dim]{messageCount} messages in context[/]")
            .LeftJustified().RuleStyle("blue"));
    }

    /// <summary>
    /// Logs the raw LLM text response (before stripping think tokens).
    /// </summary>
    public static void PrintLlmResponse(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return;
        AnsiConsole.Write(new Panel($"[blue]{Markup.Escape(rawText)}[/]")
            .Header("[blue]LLM Response[/]")
            .BorderColor(Color.Blue)
            .RoundedBorder()
            .Padding(1, 0)
            .Expand());
    }

    /// <summary>
    /// Logs the full API request body — not truncated — for debugging send payloads.
    /// </summary>
    public static void PrintApiRequest(int attempt, int max, string json)
    {
        AnsiConsole.MarkupLine($"  [dim]>> API Request[/] [grey]({attempt}/{max})[/]");
        AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(json)}[/]");
    }

    /// <summary>
    /// Logs the API response. Shows the full body when the status is not 2xx.
    /// </summary>
    public static void PrintApiResponse(int statusCode, string body)
    {
        bool success = statusCode >= 200 && statusCode < 300;
        var color = success ? "green" : "red";
        AnsiConsole.MarkupLine($"  [dim]<< API Response[/] [{color}]({statusCode})[/]");

        if (success)
        {
            // Truncate successful responses to keep output readable
            AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(Truncate(body, 500))}[/]");
        }
        else
        {
            // Always show the full body on errors so the problem is visible
            AnsiConsole.Write(new Panel($"[red]{Markup.Escape(body)}[/]")
                .Header($"[red bold]HTTP {statusCode}[/]")
                .BorderColor(Color.Red)
                .RoundedBorder()
                .Padding(1, 0));
        }
    }

    public static void PrintRateLimit(int waitMs)
    {
        AnsiConsole.MarkupLine($"  [yellow]~ Rate limit: waiting {waitMs}ms...[/]");
    }

    public static void PrintRetry(string reason)
    {
        AnsiConsole.MarkupLine($"  [yellow]~ Retry: {Markup.Escape(reason)}[/]");
    }

    public static void PrintError(string message)
    {
        AnsiConsole.Write(new Panel($"[red]{Markup.Escape(message)}[/]")
            .Header("[red bold]ERROR[/]")
            .BorderColor(Color.Red)
            .RoundedBorder()
            .Padding(1, 0));
    }

    public static void PrintResult(string result)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold green]Result[/]").RuleStyle("green"));
        AnsiConsole.Write(new Panel($"[green]{Markup.Escape(result)}[/]")
            .BorderColor(Color.Green)
            .RoundedBorder()
            .Padding(1, 0)
            .Expand());
        AnsiConsole.WriteLine();
    }

    public static void PrintInfo(string message)
    {
        AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(message)}[/]");
    }

    public static void PrintStep(string step)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[bold cyan]{Markup.Escape(step)}[/]").LeftJustified().RuleStyle("cyan"));
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
