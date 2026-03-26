using Spectre.Console;

namespace Negotiations.UI;

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

    public static void PrintIncomingRequest(string endpoint, string body)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[bold green]Incoming Request[/] [dim]{Markup.Escape(endpoint)}[/]")
            .LeftJustified().RuleStyle("green"));
        AnsiConsole.MarkupLine($"  [cyan]{Markup.Escape(Truncate(body, 500))}[/]");
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

    public static void PrintApiRequest(string json)
    {
        AnsiConsole.MarkupLine($"  [dim]>> LLM Request[/]");
        AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(Truncate(json, 300))}[/]");
    }

    public static void PrintApiResponse(string body)
    {
        AnsiConsole.MarkupLine($"  [dim]<< LLM Response[/]");
        AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(Truncate(body, 300))}[/]");
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
