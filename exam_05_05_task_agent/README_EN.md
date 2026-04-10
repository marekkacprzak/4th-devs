# TimeTravel Agent

A C# .NET 10 AI agent that acts as an interactive assistant for operating the CHRONOS-P1 time machine. The agent calculates configuration parameters, calls the device API, and instructs the human operator what to set in the web UI.

## Mission

Guide an operator through three time jumps:
1. **Jump 1** — Forward to November 5, 2238 (pick up replacement batteries)
2. **Jump 2** — Return to today (April 10, 2026)
3. **Jump 3** — Open a time tunnel/portal to November 12, 2024

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [LM Studio](https://lmstudio.ai/) with `qwen3-coder-30b-a3b-instruct-mlx` loaded at `http://localhost:1234/v1`
- Optional: OTEL collector at `http://localhost:4317` (provided by Aspire when using AppHost)

## Setup

1. Ensure `TimeTravel/.env` contains your Hub API key:
   ```
   Hub__ApiKey=<your-apikey>
   Hub__ApiUrl=https://<hub_url>
   ```

2. Ensure LM Studio is running with the required model.

## Running

```bash
# Run the agent directly
dotnet run --project TimeTravel

# Run with Aspire observability dashboard (OpenTelemetry)
dotnet run --project TimeTravel.AppHost
```

Logs are written to `TimeTravel/logs/YYYY-MM-DD_HH-mm-ss.log` with full request/response bodies.

## Architecture

Two-project solution:

```
TimeTravel/                    — Main agent (Microsoft.NET.Sdk.Web)
  Program.cs                   — Entry point, config, system prompt, agent execution
  Adapters/OpenAiClientFactory — LLM client factory (lmstudio / openai)
  Config/AgentConfig.cs        — AgentConfig, HubConfig, TelemetryConfig
  Services/
    AgentOrchestrator.cs       — Iterative LLM loop (max 25 iterations)
    HubApiClient.cs            — HTTP client for /verify API with retry/rate-limit
    RunLogger.cs               — Structured file logging
  Telemetry/TelemetrySetup.cs  — OpenTelemetry OTLP traces + metrics
  Tools/TimeTravelTools.cs     — AI-callable tools: CallVerifyApi, FetchDocumentation, CalculateSyncRatio
  UI/ConsoleUI.cs              — Spectre.Console rich output

TimeTravel.AppHost/            — .NET Aspire host (telemetry dashboard)
```

## AI Tools

| Tool | Description |
|------|-------------|
| `CallVerifyApi(action, additionalFieldsJson)` | Calls `/verify` API. Actions: `help`, `configure`, `getConfig`, `reset` |
| `FetchDocumentation()` | Fetches device manual from `https://<hub_url>/dane/timetravel.md` |
| `CalculateSyncRatio(day, month, year)` | Computes `((day*8)+(month*12)+(year*7)) % 101 / 100` |

## API Format

```json
POST https://<hub_url>/verify
{
  "apikey": "<your-apikey>",
  "task": "timetravel",
  "answer": {
    "action": "configure",
    "param": "year",
    "value": 2238
  }
}
```

API-configurable params (standby mode only): `day`, `month`, `year`, `syncRatio`, `stabilization`

Web UI only: PT-A switch, PT-B switch, PWR slider, standby/active toggle

Auto-cycling (not settable): `internalMode`

## Key Device Rules

- **Flux density** must equal 100% before any jump (happens automatically when all config is correct)
- **internalMode** cycles every few seconds: Mode1(<2000), Mode2(2000-2150), Mode3(2151-2300), Mode4(2301+)
- **PT-A** = past travel, **PT-B** = future travel, **both** = time tunnel/portal
- Battery must be **>60%** for tunnel mode
- Each jump uses approximately **1/3** of battery capacity
