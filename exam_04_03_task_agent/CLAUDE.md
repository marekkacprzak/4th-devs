# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a C# agent implementation for the AI Devs 4 exam task `domatowo`. The agent must:
1. Retrieve and analyze an 11×11 city map of the bombed town Domatowo
2. Plan an optimal route using transporters (cheap: 1pt/field) and scouts (expensive: 7pt/field)
3. Deploy scouts to inspect fields and locate a hidden survivor (in a tall building, armed, injured)
4. Call a rescue helicopter to the confirmed survivor location
5. Complete the mission within **300 action points**

## Commands

```bash
# Build
dotnet build Domatowo/Domatowo.csproj

# Run agent
dotnet run --project Domatowo/Domatowo.csproj

# Run with Aspire observability dashboard
dotnet run --project Domatowo.AppHost/Domatowo.AppHost.csproj

# Check run logs
cat Domatowo/logs/*.log
```

## Architecture

Follows the same pattern as `exam_04_02_task_agent/WindPower`. Use that project as the reference implementation.

```
Domatowo/                     # Main agent project (.NET 10, namespace Domatowo.*)
├── Program.cs                # Entry point, .env loading, service wiring
├── appsettings.json          # Config (Agent/Hub/Telemetry)
├── Adapters/OpenAiClientFactory.cs  # OpenAI SDK → IChatClient (lmstudio/openai)
├── Config/AgentConfig.cs     # AgentConfig + HubConfig POCOs
├── Config/TelemetryConfig.cs # TelemetryConfig POCO
├── Models/DomatowoModels.cs  # Map, Unit, MoveResult, InspectResult records
├── Services/DomatowoOrchestrator.cs  # C#-driven orchestrator (CORE)
├── Services/CentralaApiClient.cs     # HTTP with retry + rate-limit + OTLP tracing
├── Services/RunLogger.cs     # File-based structured logging
├── Telemetry/TelemetrySetup.cs  # TracerProvider + MeterProvider → OTLP
├── Tools/DomatowoTools.cs    # LLM-callable tool wrappers
├── UI/ConsoleUI.cs           # Spectre.Console helpers
└── logs/                     # Runtime logs

Domatowo.AppHost/             # .NET Aspire host (observability dashboard)
```

Key NuGet packages:
- `Microsoft.Agents.AI.OpenAI` v1.0.0-rc4 — Microsoft Agent Framework / IChatClient
- `OpenTelemetry` + `OpenTelemetry.Exporter.OpenTelemetryProtocol` — observability
- `Spectre.Console` — console UI

## Configuration

`.env` in `Domatowo/`:
```env
Hub__ApiKey=<your-key>
Hub__ApiUrl=https://<hub_url>
```

`appsettings.json` structure:
```json
{
  "Agent":  { "Provider": "lmstudio", "Model": "qwen3-coder-30b-a3b-instruct-mlx", "Endpoint": "http://localhost:1234/v1" },
  "Hub":    { "TaskName": "domatowo", "MaxRetries": 3, "RetryDelayMs": 500 },
  "Telemetry": { "Enabled": true, "ServiceName": "Domatowo", "EnableSensitiveData": true }
}
```

LLM runs locally via LM Studio at `http://localhost:1234/v1`.

## API Protocol

All hub calls POST JSON to `Hub__ApiUrl/verify`:

```json
{
  "apikey": "<Hub__ApiKey>",
  "task": "domatowo",
  "answer": { "action": "..." }
}
```

Key actions:
- `help` — list available actions
- `getMap` (optional `symbols` array) — retrieve 11×11 map
- `create` with `type: "transporter"` + `passengers: N` — costs 5 + (5×N) pts
- `create` with `type: "scout"` — costs 5 pts
- `move` — move a unit; transporter: 1pt/field, scout: 7pt/field
- `inspect` — inspect current field for survivor; costs 1 pt
- `getLogs` — retrieve action history
- `callHelicopter` with `destination: "F6"` — finalize mission (only after scout confirms survivor)

## Resource Constraints

| Resource | Limit |
|---|---|
| Action points | 300 total |
| Transporters | 4 max |
| Scouts | 8 max |
| Map size | 11×11 |

Transporters travel roads only. Scouts move anywhere but are expensive (7pt/field). Use transporters to carry scouts to search zones, then deploy scouts on foot for local inspection.

## Survivor Intel

Intercepted signal: survivor is in **one of the tallest buildings**, is armed and injured. Prioritize high-rise / tall building map symbols when planning search order.

## Custom Slash Commands

- `/plan` — systematic planning with user approval before any implementation
- `/implement` — step-by-step execution of an approved plan with rollback support
