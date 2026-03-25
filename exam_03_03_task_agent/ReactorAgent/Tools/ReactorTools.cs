using System.ComponentModel;
using Microsoft.Extensions.AI;
using ReactorAgent.Models;
using ReactorAgent.Services;
using ReactorAgent.UI;

namespace ReactorAgent.Tools;

public class ReactorTools
{
    private readonly HubApiClient _hubApi;
    private readonly ReactorNavigator _navigator;

    private ReactorBoard? _currentBoard;
    private string _lastRawResponse = "";

    public ReactorTools(HubApiClient hubApi, ReactorNavigator navigator)
    {
        _hubApi = hubApi;
        _navigator = navigator;
    }

    [Description("Send a command to the reactor robot. Valid commands: start (initialize session), reset (restart), left (move robot left), wait (hold position, blocks advance), right (move robot right). Returns the new board state after the command.")]
    public async Task<string> SendCommand(
        [Description("The command to send. One of: start, reset, left, wait, right")] string command)
    {
        ConsoleUI.PrintToolCall("SendCommand", command);
        var response = await _hubApi.SendCommandAsync(command);
        _lastRawResponse = response;

        _currentBoard = _navigator.ParseBoard(response);
        ConsoleUI.PrintReactorBoard(_currentBoard);

        if (response.Contains("{FLG:") || response.Contains("\"reached_goal\": true") || response.Contains("\"reached_goal\":true"))
            return $"SUCCESS! {response}";

        var grid = _currentBoard.ToTextGrid();
        var robotPos = $"Robot at column {_currentBoard.RobotColumn} (row 5). Goal: column 7.";
        var blocksInfo = _currentBoard.Blocks.Count > 0
            ? $"Blocks: {string.Join("; ", _currentBoard.Blocks.Select(b => $"col={b.Column} rows={b.TopRow}-{b.BottomRow} dir={b.MoveDirection}"))}"
            : "No blocks detected.";

        return $"{robotPos}\n{blocksInfo}\n\nBoard:\n{grid}\n\nRaw response: {response}";
    }

    [Description("Analyze the current board state and return the safest next command for the robot. Uses one-step lookahead to check if target cell will be safe after blocks move.")]
    public string DecideNextMove()
    {
        ConsoleUI.PrintToolCall("DecideNextMove");

        if (_currentBoard == null)
            return "start";

        if (_currentBoard.IsGoalReached)
            return "done";

        var move = _navigator.DecideNextMove(_currentBoard);
        ConsoleUI.PrintInfo($"Navigator recommends: {move}");
        return move;
    }

    [Description("Get the current board state as a text visualization. Returns robot position, block positions and directions, and a grid overview.")]
    public string GetBoardState()
    {
        ConsoleUI.PrintToolCall("GetBoardState");

        if (_currentBoard == null)
            return "No board state yet. Call SendCommand with 'start' first.";

        var grid = _currentBoard.ToTextGrid();
        var robotPos = $"Robot at column {_currentBoard.RobotColumn} (row 5). Goal: column 7.";
        var blocksInfo = _currentBoard.Blocks.Count > 0
            ? $"Blocks: {string.Join("; ", _currentBoard.Blocks.Select(b => $"col={b.Column} rows={b.TopRow}-{b.BottomRow} dir={b.MoveDirection}"))}"
            : "No blocks detected.";

        return $"{robotPos}\n{blocksInfo}\n\nBoard:\n{grid}";
    }

    /// <summary>Expose the tools as AIFunctions for the Microsoft Agent Framework.</summary>
    public IEnumerable<AIFunction> GetAIFunctions()
    {
        yield return AIFunctionFactory.Create(SendCommand);
        yield return AIFunctionFactory.Create((Func<string>)DecideNextMove);
        yield return AIFunctionFactory.Create((Func<string>)GetBoardState);
    }

    public ReactorBoard? CurrentBoard => _currentBoard;
    public string LastRawResponse => _lastRawResponse;
}
