using System.ComponentModel;
using FirmwareAgent.Services;
using FirmwareAgent.UI;

namespace FirmwareAgent.Tools;

public class ShellTools
{
    private readonly HubApiClient _api;

    // Forbidden path prefixes — accessing these causes a ban + VM reset
    private static readonly string[] ForbiddenPathPrefixes = ["/etc", "/root", "/proc"];

    // Forbidden file name patterns — always forbidden regardless of location
    private static readonly string[] ForbiddenFileNames = [".env"];

    public ShellTools(HubApiClient api)
    {
        _api = api;
    }

    [Description(
        "Execute a shell command on the virtual Linux machine and return the output. " +
        "SECURITY RULES (violation = ban + VM reset): " +
        "NEVER access /etc, /root, or /proc/ directories. " +
        "NEVER access .env files — they are ALWAYS forbidden. " +
        "Always read .gitignore before accessing files in any directory and skip listed files. " +
        "COMMAND SYNTAX — this VM has non-standard commands: " +
        "'find <pattern>' searches by filename pattern only (e.g. 'find *.ini', 'find password*'). " +
        "Do NOT use Unix find flags like -type, -name, -exec — they will fail. " +
        "'editline <file> <line-number> <content>' replaces one line in a file. " +
        "'cat <path>' shows file content. 'ls [path]' lists directory.")]
    public async Task<string> ExecuteCommand(
        [Description("The shell command to execute, e.g. 'help', 'ls /opt/firmware/cooler', 'cat /opt/firmware/cooler/settings.ini', 'find *.ini'")] string command)
    {
        var blocked = CheckForbidden(command);
        if (blocked is not null)
        {
            ConsoleUI.PrintError($"BLOCKED (pre-flight): {blocked}");
            return blocked;
        }

        ConsoleUI.PrintToolCall("ExecuteCommand", $"cmd={command}");
        var result = await _api.ExecuteShellCommandAsync(command);
        ConsoleUI.PrintInfo($"Output: {ConsoleUI.Truncate(result, 600)}");
        return result;
    }

    private static string? CheckForbidden(string command)
    {
        // Normalize for matching (keep original case for message)
        var cmd = command.Trim();

        foreach (var prefix in ForbiddenPathPrefixes)
        {
            // Match /etc, /etc/, /etc/something
            if (ContainsPath(cmd, prefix))
                return $"BLOCKED: Command references forbidden path '{prefix}'. Never access /etc, /root, or /proc.";
        }

        foreach (var name in ForbiddenFileNames)
        {
            if (ContainsFileName(cmd, name))
                return $"BLOCKED: '{name}' is a forbidden file (listed in .gitignore). Do not attempt to access it.";
        }

        return null;
    }

    private static bool ContainsPath(string command, string forbiddenPrefix)
    {
        // Match the path segment anywhere in the command
        var idx = command.IndexOf(forbiddenPrefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;
        // Make sure it's a path boundary: char after prefix is '/', ' ', end-of-string, or prefix is at end
        var afterIdx = idx + forbiddenPrefix.Length;
        if (afterIdx >= command.Length) return true;
        var next = command[afterIdx];
        return next == '/' || next == ' ' || next == '\t' || next == '"' || next == '\'';
    }

    private static bool ContainsFileName(string command, string forbiddenName)
    {
        return command.Contains(forbiddenName, StringComparison.OrdinalIgnoreCase);
    }
}
