using System.ComponentModel;
using ShellAccess.Services;
using ShellAccess.UI;

namespace ShellAccess.Tools;

public class ShellTools
{
    private readonly CentralaApiClient _centrala;

    public ShellTools(CentralaApiClient centrala)
    {
        _centrala = centrala;
    }

    [Description("Execute a shell command on the remote server and return its output. " +
                 "The server has standard Linux tools: ls, find, cat, grep, head, tail, wc, jq, awk, sed, date, echo. " +
                 "Files are located in the /data directory. " +
                 "Avoid cat-ing large files — use grep, head, or tail to limit output.")]
    public async Task<string> ExecuteShellCommand(
        [Description("The shell command to execute on the remote server, e.g. 'ls -la /data' or 'grep -r Rafal /data' or 'echo \"{\\\"date\\\":\\\"2020-01-01\\\"}\\\"'")]
        string command)
    {
        ConsoleUI.PrintStep($"Shell: {command}");
        return await _centrala.VerifyAsync(new { cmd = command });
    }
}
