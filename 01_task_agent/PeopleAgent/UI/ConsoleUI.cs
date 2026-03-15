using Spectre.Console;

namespace PeopleAgent.UI;

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

    public static void PrintApiRequest(int attempt, int max, string json)
    {
        AnsiConsole.MarkupLine($"  [dim]>> API Request[/] [grey]({attempt}/{max})[/]");
        AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(Truncate(json, 200))}[/]");
    }

    public static void PrintApiResponse(int statusCode, string body)
    {
        var color = statusCode >= 200 && statusCode < 300 ? "green" : "red";
        AnsiConsole.MarkupLine($"  [dim]<< API Response[/] [{color}]({statusCode})[/]");
        AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(Truncate(body, 300))}[/]");
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
