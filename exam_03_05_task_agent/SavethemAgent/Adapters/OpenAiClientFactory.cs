using System.ClientModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using SavethemAgent.Config;

namespace SavethemAgent.Adapters;

public static class OpenAiClientFactory
{
    public static (IChatClient client, LoggingChatClient? logger) CreateChatClientWithLogger(
        AgentConfig config,
        TelemetryConfig? telemetryConfig = null,
        string? logsDirectory = null)
    {
        var client = BuildChatClient(config, telemetryConfig, logsDirectory, out var logger);
        return (client, logger);
    }

    private static IChatClient BuildChatClient(
        AgentConfig config,
        TelemetryConfig? telemetryConfig,
        string? logsDirectory,
        out LoggingChatClient? loggingClient)
    {
        var openAiClient = CreateOpenAiClient(config);
        IChatClient chatClient = openAiClient
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

        if (logsDirectory != null)
        {
            loggingClient = new LoggingChatClient(chatClient, logsDirectory);
            return loggingClient;
        }

        loggingClient = null;
        return chatClient;
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
