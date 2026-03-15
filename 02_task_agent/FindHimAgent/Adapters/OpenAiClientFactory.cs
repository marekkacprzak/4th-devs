using System.ClientModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using FindHimAgent.Config;

namespace FindHimAgent.Adapters;

public static class OpenAiClientFactory
{
    public static AIAgent CreateAgent(AgentConfig config, string instructions, IEnumerable<AITool> tools)
    {
        var client = CreateOpenAiClient(config);

        return client
            .GetChatClient(config.Model)
            .AsIChatClient()
            .AsAIAgent(
                name: "FindHimAgent",
                instructions: instructions,
                tools: tools.ToList());
    }

    public static IChatClient CreateChatClient(AgentConfig config)
    {
        var client = CreateOpenAiClient(config);

        return client
            .GetChatClient(config.Model)
            .AsIChatClient();
    }

    public static OpenAIClient CreateOpenAiClient(AgentConfig config)
    {
        return config.Provider.ToLowerInvariant() switch
        {
            "openai" => new OpenAIClient(config.GetApiKey()),
            "lmstudio" => new OpenAIClient(
                new ApiKeyCredential("lm-studio"),
                new OpenAIClientOptions { Endpoint = new Uri(config.Endpoint) }),
            _ => throw new InvalidOperationException(
                $"Unknown provider '{config.Provider}'. Supported: openai, lmstudio")
        };
    }
}
