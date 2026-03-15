using System.ComponentModel;
using RailwayAgent.Services;
using RailwayAgent.UI;

namespace RailwayAgent.Tools;

public class RailwayApiTools
{
    private readonly RailwayApiClient _api;

    public RailwayApiTools(RailwayApiClient api)
    {
        _api = api;
    }

    [Description("Show available Railway API actions, parameters, and documentation")]
    public async Task<string> Help()
    {
        ConsoleUI.PrintToolCall("Help");

        return await _api.SendAsync(new Dictionary<string, string>
        {
            ["action"] = "help"
        });
    }

    [Description("Enable reconfigure mode for a given railway route. Must be called before SetStatus.")]
    public async Task<string> Reconfigure(
        [Description("Route code in format like x-01, x-02 etc.")] string route)
    {
        ConsoleUI.PrintToolCall("Reconfigure", $"route={route}");

        return await _api.SendAsync(new Dictionary<string, string>
        {
            ["action"] = "reconfigure",
            ["route"] = route
        });
    }

    [Description("Get current status (RTOPEN or RTCLOSE) for a given railway route")]
    public async Task<string> GetStatus(
        [Description("Route code in format like x-01, x-02 etc.")] string route)
    {
        ConsoleUI.PrintToolCall("GetStatus", $"route={route}");

        return await _api.SendAsync(new Dictionary<string, string>
        {
            ["action"] = "getstatus",
            ["route"] = route
        });
    }

    [Description("Set route status while in reconfigure mode. Call Reconfigure first. Allowed values: RTOPEN, RTCLOSE")]
    public async Task<string> SetStatus(
        [Description("Route code in format like x-01, x-02 etc.")] string route,
        [Description("Status value: RTOPEN (to open) or RTCLOSE (to close)")] string value)
    {
        ConsoleUI.PrintToolCall("SetStatus", $"route={route}, value={value}");

        return await _api.SendAsync(new Dictionary<string, string>
        {
            ["action"] = "setstatus",
            ["route"] = route,
            ["value"] = value
        });
    }

    [Description("Exit reconfigure mode and save changes for a given route. Call after SetStatus.")]
    public async Task<string> Save(
        [Description("Route code in format like x-01, x-02 etc.")] string route)
    {
        ConsoleUI.PrintToolCall("Save", $"route={route}");

        return await _api.SendAsync(new Dictionary<string, string>
        {
            ["action"] = "save",
            ["route"] = route
        });
    }
}
