using System.ClientModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using DroneAgent.Config;

namespace DroneAgent.Adapters;

public static class OpenAiClientFactory
{
    public static AIAgent CreateAgent(
        AgentConfig config,
        string instructions,
        IEnumerable<AITool> tools,
        TelemetryConfig? telemetryConfig = null,
        string? logFilePath = null)
    {
        var chatClient = CreateChatClient(config, telemetryConfig, logFilePath);

        return chatClient.AsAIAgent(
            name: "DroneAgent",
            instructions: instructions,
            tools: tools.ToList());
    }

    public static IChatClient CreateChatClient(AgentConfig config, TelemetryConfig? telemetryConfig = null, string? logFilePath = null)
    {
        var client = CreateOpenAiClient(config.Provider, config.Endpoint, config.GetApiKey());

        IChatClient chatClient = client
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

        chatClient = new LoggingChatClient(chatClient, "Agent", logFilePath);

        return chatClient;
    }

    public static IChatClient CreateVisionChatClient(VisionConfig config, TelemetryConfig? telemetryConfig = null, string? logFilePath = null)
    {
        var client = CreateOpenAiClient(config.Provider, config.Endpoint, config.GetApiKey());

        IChatClient chatClient = client
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

        chatClient = new LoggingChatClient(chatClient, "Vision", logFilePath);

        return chatClient;
    }

    private static OpenAIClient CreateOpenAiClient(string provider, string endpoint, string apiKey)
    {
        return provider.ToLowerInvariant() switch
        {
            "openai" => new OpenAIClient(apiKey),
            "lmstudio" => new OpenAIClient(
                new ApiKeyCredential("lm-studio"),
                new OpenAIClientOptions
                {
                    Endpoint = new Uri(endpoint),
                    NetworkTimeout = TimeSpan.FromMinutes(5)
                }),
            _ => throw new InvalidOperationException(
                $"Unknown provider '{provider}'. Supported: openai, lmstudio")
        };
    }
}
