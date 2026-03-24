# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Task Overview

Build a C# agent using **Microsoft Agent Framework** that connects to a virtual Linux machine via shell API, navigates the filesystem, runs `/opt/firmware/cooler/cooler.bin`, and submits the resulting confirmation code (`ECCS-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx`) to the hub.

- Shell API: `https://<hubApi>/api/shell` — POST `{ "apikey": "...", "cmd": "help" }`
- Submit API: `https://<hubApi>/verify` — POST `{ "apikey": "...", "task": "firmware", "answer": { "confirmation": "..." } }`
- Security rules: never touch `/etc`, `/root`, `/proc/`; respect `.gitignore` files in any directory
- Start with `help` command — this VM has a non-standard command set; don't assume standard Linux commands

## Reference Architecture

Follow the pattern from `../exam_02_03_task_agent/` (FailureAgent). The expected solution structure:

```
FirmwareAgent/                   # Main agent project
├── Program.cs                   # Entry point, explicit DI wiring, agent loop
├── FirmwareAgent.csproj
├── appsettings.json
├── Adapters/
│   └── OpenAiClientFactory.cs   # Provider abstraction (lmstudio / openai)
├── Config/
│   ├── AgentConfig.cs           # LLM provider/model/endpoint
│   ├── HubConfig.cs             # API URL, apikey, retry settings
│   └── TelemetryConfig.cs       # OTLP endpoint, service name
├── Services/
│   └── HubApiClient.cs          # HTTP with exponential backoff, rate-limit handling
├── Telemetry/
│   └── TelemetrySetup.cs        # OpenTelemetry TracerProvider + MeterProvider
├── Tools/
│   ├── ShellTools.cs            # execute_command tool wrapping shell API
│   └── SubmitTools.cs           # submit_answer tool
└── UI/
    └── ConsoleUI.cs             # Spectre.Console rich output

FirmwareAgent.AppHost/           # .NET Aspire host
├── AppHost.cs
└── FirmwareAgent.AppHost.csproj
```

## Commands

```bash
dotnet build                                    # Build all projects
dotnet run --project FirmwareAgent              # Run agent directly
dotnet run --project FirmwareAgent.AppHost      # Run with Aspire (recommended for telemetry)
dotnet restore                                  # Restore NuGet packages
```

## Key NuGet Packages

```xml
<TargetFramework>net10.0</TargetFramework>
<PackageReference Include="Microsoft.Agents.AI.OpenAI" Version="1.0.0-rc4" />
<PackageReference Include="Microsoft.Extensions.Configuration" Version="10.0.5" />
<PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.5" />
<PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="10.0.5" />
<PackageReference Include="OpenTelemetry" Version="1.*" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.*" />
<PackageReference Include="Spectre.Console" Version="0.54.0" />
```

Aspire AppHost uses `<Sdk>Aspire.AppHost.Sdk/13.1.2</Sdk>`.

## Configuration (`appsettings.json`)

```json
{
  "Agent": {
    "Provider": "lmstudio",
    "Model": "qwen3-coder-30b-a3b-instruct-mlx",
    "Endpoint": "http://localhost:1234/v1"
  },
  "Hub": {
    "ApiUrl": "https://<hubApi>",
    "ApiKey": "",
    "TaskName": "firmware",
    "MaxRetries": 5,
    "RetryDelayMs": 2000
  },
  "Telemetry": {
    "Enabled": true,
    "OtlpEndpoint": "http://localhost:4317",
    "ServiceName": "FirmwareAgent",
    "EnableSensitiveData": true
  }
}
```

API key goes in `.env` file (parsed manually in Program.cs) or environment variable.

## Architecture Patterns

**No DI container** — wire services explicitly in `Program.cs`:
```csharp
var hubApi = new HubApiClient(httpClient, hubConfig);
var chatClient = OpenAiClientFactory.CreateChatClient(agentConfig, telemetryConfig);
```

**Config binding** — use `configuration.GetSection("Agent").Bind(agentConfig)` with strongly-typed classes.

**API key fallback** in `AgentConfig.GetApiKey()`: config value → `OPENAI_API_KEY` env var → `"lm-studio"`.

**Telemetry** — wrap `IChatClient` with OpenTelemetry middleware if enabled; create activity sources per service (e.g., `new ActivitySource("FirmwareAgent.Hub")`).

**Retry logic** in `HubApiClient`:
- HTTP 503 → exponential backoff (delay × 2^attempt, capped at 30s)
- HTTP 429 → respect `Retry-After` header or `retry_after` JSON field
- Ban violations return error with seconds to wait; surface these to the agent as descriptive messages

**Agent loop** — function calling pattern: agent calls `execute_command` (one shell command per call), analyzes response, decides next action. Use `anthropic/claude-sonnet-4-6` or configure via `Agent.Model`.

**Tool pattern** — `[Description]` attributes on tool methods enable the agent framework to discover them.
