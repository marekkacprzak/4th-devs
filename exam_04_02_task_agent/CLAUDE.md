# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a C# agent implementation for the AI Devs 4 exam task `windpower`. The agent must:
1. Analyze weather forecasts to identify dangerous wind conditions
2. Configure wind turbine pitch angles (90°) during storms to protect blades
3. Find optimal weather windows for power production
4. Complete all work within a **40-second battery window** — parallelization is mandatory

## Commands

```bash
# Build
dotnet build WindPower/WindPower.csproj

# Run agent
dotnet run --project WindPower/WindPower.csproj

# Run with Aspire observability dashboard
dotnet run --project WindPower.AppHost/WindPower.AppHost.csproj

# Check run logs
cat WindPower/logs/*.log
```

## Architecture

```
WindPower/                    # Main agent project (.NET 10, namespace WindPower.*)
├── Program.cs                # Entry point, .env loading, service wiring
├── appsettings.json          # Config (Agent/Hub/Telemetry)
├── Adapters/OpenAiClientFactory.cs  # OpenAI SDK → IChatClient (lmstudio/openai)
├── Config/AgentConfig.cs     # AgentConfig + HubConfig POCOs
├── Config/TelemetryConfig.cs # TelemetryConfig POCO
├── Models/VerifyRequest.cs   # DTO
├── Models/WindpowerModels.cs # WeatherEntry, TurbineSpecsData, ConfigEntry records
├── Services/WindpowerOrchestrator.cs  # C#-driven 4-phase orchestrator (CORE)
├── Services/CentralaApiClient.cs     # HTTP with retry + rate-limit + OTLP tracing
├── Services/RunLogger.cs     # File-based structured logging
├── Telemetry/TelemetrySetup.cs  # TracerProvider + MeterProvider → OTLP
├── Tools/WindpowerTools.cs   # CallVerifyApi tool (LLM-callable)
├── UI/ConsoleUI.cs           # Spectre.Console helpers
└── logs/                     # Runtime logs

WindPower.AppHost/            # .NET Aspire host (observability dashboard port 5010)
```

Key NuGet packages:
- `Microsoft.Agents.AI.OpenAI` v1.0.0-rc4 — Microsoft Agent Framework / IChatClient
- `OpenTelemetry` + `OpenTelemetry.Exporter.OpenTelemetryProtocol` — observability
- `Spectre.Console` — console UI

## Configuration

`.env` in `WindPower/` (copied from root):
```env
Hub__ApiKey=<your-key>
Hub__ApiUrl=https://<hub_url>
```

`appsettings.json` structure:
```json
{
  "Agent": { "Provider": "lmstudio", "Model": "qwen3-coder-30b-a3b-instruct-mlx", "Endpoint": "http://localhost:1234/v1" },
  "Hub":   { "TaskName": "windpower", "MaxRetries": 3, "RetryDelayMs": 500 },
  "Telemetry": { "Enabled": true, "ServiceName": "WindPower" }
}
```

LLM runs locally via LM Studio at `http://localhost:1234/v1`.

## API Protocol

All Centrala hub calls POST JSON to `Hub__ApiUrl/verify`:

```json
{
  "apikey": "<Hub__ApiKey>",
  "task": "windpower",
  "answer": { "action": "..." }
}
```

Key actions: `help` → `start` → `config` (with turbine settings) → `turbinecheck` → `done`.

The `config` action requires a digitally-signed `unlockCode` from `unlockCodeGenerator`.
Most actions are **async/queue-based** — use `getResult` to poll for responses.

## Critical Constraint

The 40-second window requires parallel execution. `WindpowerOrchestrator` uses `Task.WhenAll` to fire all data requests and unlock code requests simultaneously, then polls `getResult` in a tight loop.

## Custom Slash Commands

- `/plan` — systematic planning with user approval before any implementation
- `/implement` — step-by-step execution of an approved plan with rollback support
