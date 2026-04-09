using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using GoingThere.UI;

namespace GoingThere.Services;

public class AgentOrchestrator
{
    private readonly IChatClient _chatClient;
    private readonly IList<AITool> _tools;
    private readonly string _systemPrompt;
    private readonly RunLogger _logger;
    private const int MaxIterations = 80;

    public AgentOrchestrator(IChatClient chatClient, IList<AITool> tools, string systemPrompt, RunLogger logger)
    {
        _chatClient = chatClient;
        _tools = tools;
        _systemPrompt = systemPrompt;
        _logger = logger;
    }

    public async Task<string> RunAgentAsync(string userGoal)
    {
        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, _systemPrompt),
            new ChatMessage(ChatRole.User, userGoal)
        };

        var options = new ChatOptions
        {
            Tools = _tools
        };

        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            ConsoleUI.PrintLlmRequest(iteration + 1, MaxIterations, messages.Count);
            _logger.LogLlmRequest(iteration + 1, MaxIterations, messages.Count);

            ChatResponse response;
            try
            {
                response = await _chatClient.GetResponseAsync(messages, options);
            }
            catch (Exception ex)
            {
                ConsoleUI.PrintError($"LLM error: {ex.Message}\n\nFull exception:\n{ex}");
                _logger.LogError("LLM", ex.ToString());
                return $"ERROR: LLM call failed: {ex.Message}";
            }

            // Collect tool calls from the response
            var toolCalls = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .ToList();

            // Log any raw text from this response
            var rawText = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<TextContent>()
                .Select(t => t.Text)
                .LastOrDefault();

            if (!string.IsNullOrWhiteSpace(rawText))
            {
                ConsoleUI.PrintLlmResponse(rawText);
                _logger.LogLlmResponse(rawText);
            }

            if (toolCalls.Count == 0)
            {
                var finalText = StripThinkingTokens(rawText ?? "");
                _logger.LogInfo($"Agent finished. Final response length: {finalText.Length} chars");
                return finalText;
            }

            // Add assistant message with tool calls to conversation
            var assistantMsg = new ChatMessage(ChatRole.Assistant,
                response.Messages.SelectMany(m => m.Contents).ToList());
            messages.Add(assistantMsg);

            // Execute each tool call
            foreach (var toolCall in toolCalls)
            {
                var argsJson = toolCall.Arguments != null
                    ? JsonSerializer.Serialize(toolCall.Arguments)
                    : null;

                ConsoleUI.PrintToolCallDetail(toolCall.Name, toolCall.CallId, argsJson);
                _logger.LogToolCall(toolCall.Name, toolCall.CallId, argsJson);

                var tool = _tools.OfType<AIFunction>().FirstOrDefault(t => t.Name == toolCall.Name);
                string resultStr;
                bool isError = false;

                if (tool != null)
                {
                    try
                    {
                        var args = toolCall.Arguments != null
                            ? new AIFunctionArguments(toolCall.Arguments)
                            : null;
                        var result = await tool.InvokeAsync(args);
                        resultStr = result?.ToString() ?? "";
                    }
                    catch (Exception ex)
                    {
                        resultStr = $"Error: {ex.Message}\n\nStack trace:\n{ex.StackTrace}";
                        isError = true;
                        ConsoleUI.PrintError($"Tool execution error: {ex.Message}");
                    }
                }
                else
                {
                    resultStr = $"Error: unknown tool '{toolCall.Name}'";
                    isError = true;
                }

                ConsoleUI.PrintToolResult(toolCall.CallId, resultStr, isError);
                _logger.LogToolResult(toolCall.CallId, resultStr, isError);

                var toolResultContent = new FunctionResultContent(toolCall.CallId, resultStr);
                var toolMsg = new ChatMessage(ChatRole.Tool, [toolResultContent]);
                messages.Add(toolMsg);
            }
        }

        return "ERROR: Maximum iterations reached without completing the navigation.";
    }

    private static string StripThinkingTokens(string text)
    {
        return Regex.Replace(text, @"<think>[\s\S]*?</think>", "").Trim();
    }
}
