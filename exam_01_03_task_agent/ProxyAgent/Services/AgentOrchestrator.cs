using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using ProxyAgent.UI;

namespace ProxyAgent.Services;

public class AgentOrchestrator
{
    private readonly IChatClient _chatClient;
    private readonly IList<AITool> _tools;
    private readonly string _systemPrompt;
    private const int MaxIterations = 5;

    public AgentOrchestrator(IChatClient chatClient, IList<AITool> tools, string systemPrompt)
    {
        _chatClient = chatClient;
        _tools = tools;
        _systemPrompt = systemPrompt;
    }

    public async Task<string> ProcessMessageAsync(string sessionId, string userMessage, SessionManager sessionManager)
    {
        sessionManager.AddMessage(sessionId, new ChatMessage(ChatRole.User, userMessage));

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, _systemPrompt)
        };
        messages.AddRange(sessionManager.GetMessages(sessionId));

        var options = new ChatOptions
        {
            Tools = _tools
        };

        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            ConsoleUI.PrintInfo($"LLM call iteration {iteration + 1}/{MaxIterations}");

            ChatResponse response;
            try
            {
                response = await _chatClient.GetResponseAsync(messages, options);
            }
            catch (Exception ex)
            {
                ConsoleUI.PrintError($"LLM error: {ex.Message}");
                return "Przepraszam, wystąpił problem. Spróbuj ponownie.";
            }

            // Collect tool calls from the response
            var toolCalls = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .ToList();

            if (toolCalls.Count == 0)
            {
                // Final text response
                var textContent = response.Messages
                    .SelectMany(m => m.Contents)
                    .OfType<TextContent>()
                    .Select(t => t.Text)
                    .LastOrDefault() ?? "";

                textContent = StripThinkingTokens(textContent);

                sessionManager.AddMessage(sessionId, new ChatMessage(ChatRole.Assistant, textContent));
                ConsoleUI.PrintInfo($"Final response: {textContent}");
                return textContent;
            }

            // Add assistant message with tool calls to conversation
            var assistantMsg = new ChatMessage(ChatRole.Assistant,
                response.Messages.SelectMany(m => m.Contents).ToList());
            messages.Add(assistantMsg);
            sessionManager.AddMessage(sessionId, assistantMsg);

            // Execute each tool call
            foreach (var toolCall in toolCalls)
            {
                ConsoleUI.PrintInfo($"Executing tool: {toolCall.Name} (callId: {toolCall.CallId})");

                var tool = _tools.OfType<AIFunction>().FirstOrDefault(t => t.Name == toolCall.Name);
                string resultStr;
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
                        resultStr = $"Error: {ex.Message}";
                        ConsoleUI.PrintError($"Tool execution error: {ex.Message}");
                    }
                }
                else
                {
                    resultStr = $"Error: unknown tool '{toolCall.Name}'";
                }

                var toolResultContent = new FunctionResultContent(toolCall.CallId, resultStr);
                var toolMsg = new ChatMessage(ChatRole.Tool, [toolResultContent]);
                messages.Add(toolMsg);
                sessionManager.AddMessage(sessionId, toolMsg);
            }
        }

        var fallback = "Przepraszam, muszę się chwilę zastanowić. Spróbuj ponownie.";
        sessionManager.AddMessage(sessionId, new ChatMessage(ChatRole.Assistant, fallback));
        return fallback;
    }

    private static string StripThinkingTokens(string text)
    {
        return Regex.Replace(text, @"<think>[\s\S]*?</think>", "").Trim();
    }
}
