using System.ClientModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using EvaluationAgent.Config;

namespace EvaluationAgent.Adapters;

public static class OpenAiClientFactory
{
    public static AIAgent CreateAgent(
        AgentConfig config,
        string instructions,
        IEnumerable<AITool> tools,
        TelemetryConfig? telemetryConfig = null)
    {
        var client = CreateOpenAiClient(config);

        var chatClient = client
            .GetChatClient(config.Model)
            .AsIChatClient();

        if (telemetryConfig is { Enabled: true })
        {
            chatClient = chatClient
                .AsBuilder()
                .UseOpenTelemetry(
                    sourceName: telemetryConfig.ServiceName,
                    configure: c => c.EnableSensitiveData = telemetryConfig.EnableSensitiveData)
                .Build();
        }

        return chatClient.AsAIAgent(
            name: "EvaluationAgent",
            instructions: instructions,
            tools: tools.ToList());
    }

    public static IChatClient CreateChatClient(AgentConfig config, TelemetryConfig? telemetryConfig = null)
    {
        var client = CreateOpenAiClient(config);

        var chatClient = client
            .GetChatClient(config.Model)
            .AsIChatClient();

        if (telemetryConfig is { Enabled: true })
        {
            chatClient = chatClient
                .AsBuilder()
                .UseOpenTelemetry(
                    sourceName: telemetryConfig.ServiceName,
                    configure: c => c.EnableSensitiveData = telemetryConfig.EnableSensitiveData)
                .Build();
        }

        return chatClient;
    }

    public static OpenAIClient CreateOpenAiClient(AgentConfig config)
    {
        return config.Provider.ToLowerInvariant() switch
        {
            "openai" => new OpenAIClient(config.GetApiKey()),
            "lmstudio" => new OpenAIClient(
                new ApiKeyCredential("lm-studio"),
                new OpenAIClientOptions
                {
                    Endpoint = new Uri(config.Endpoint),
                    NetworkTimeout = TimeSpan.FromMinutes(5)
                }),
            _ => throw new InvalidOperationException(
                $"Unknown provider '{config.Provider}'. Supported: openai, lmstudio")
        };
    }
}
