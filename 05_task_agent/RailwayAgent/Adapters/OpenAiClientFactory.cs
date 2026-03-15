using System.ClientModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using RailwayAgent.Config;

namespace RailwayAgent.Adapters;

public static class OpenAiClientFactory
{
    public static AIAgent CreateAgent(AgentConfig config, string instructions, IEnumerable<AITool> tools)
    {
        var client = config.Provider.ToLowerInvariant() switch
        {
            "openai" => new OpenAIClient(config.GetApiKey()),
            "lmstudio" => new OpenAIClient(
                new ApiKeyCredential("lm-studio"),
                new OpenAIClientOptions { Endpoint = new Uri(config.Endpoint) }),
            _ => throw new InvalidOperationException(
                $"Unknown provider '{config.Provider}'. Supported: openai, lmstudio")
        };

        return client
            .GetChatClient(config.Model)
            .AsIChatClient()
            .AsAIAgent(
                name: "RailwayAgent",
                instructions: instructions,
                tools: tools.ToList());
    }
}
