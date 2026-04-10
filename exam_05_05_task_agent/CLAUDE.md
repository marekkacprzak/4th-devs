# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Task Overview

**TimeTravel** — a C# .NET 10 AI agent that acts as an assistant for operating a time machine. It does NOT automate the jumps itself; instead, it instructs a human operator what to configure in the web UI and verifies the configuration via API before each jump. Three jumps required:
1. Jump to November 5, 2238 (battery pickup)
2. Return to today's date
3. Open a time portal to November 12, 2024

- Device documentation: `https://<hub_url>/dane/timetravel.md` (read this first — contains syncRatio formula, PWR table, PT-A/PT-B rules, flux density requirements)
- Web UI: `https://<hub_url>/timetravel_preview` (operator-controlled)
- Verification endpoint: `https://<hub_url>/verify`
- Task name for API: `timetravel`

## Commands

```bash
# Build
dotnet build

# Run the agent (primary entry point)
dotnet run --project TimeTravel

# Run with Aspire dashboard + OTLP telemetry
dotnet run --project TimeTravel.AppHost

# Restore dependencies
dotnet restore
```

No test project is expected. Logs are written to `TimeTravel/logs/YYYY-MM-DD_HH-mm-ss.log`.

## Architecture

Two-project solution mirroring `exam_04_01_task_agent/OkoEditor`:

```
TimeTravel/                    ← Main agent (Microsoft.NET.Sdk.Web, net10.0)
  Program.cs                   ← Loads .env, builds config, wires services, runs loop
  appsettings.json
  Adapters/OpenAiClientFactory.cs   ← Creates IChatClient for lmstudio/openai
  Config/                      ← AgentConfig, HubConfig, TelemetryConfig
  Models/                      ← API request/response DTOs
  Services/
    AgentOrchestrator.cs       ← Agent loop: max 15 iterations, terminates on no tool calls
    HubApiClient.cs            ← HTTP client for /verify with retry + rate-limit handling
    RunLogger.cs               ← File logging with structured tags
  Telemetry/TelemetrySetup.cs  ← TracerProvider + MeterProvider → OTLP
  Tools/TimeTravelTools.cs     ← AI-callable functions (CallVerifyApi, FetchDocumentation)
  UI/ConsoleUI.cs              ← Spectre.Console rich output
  logs/                        ← Runtime log files (gitignored)

TimeTravel.AppHost/            ← Aspire host (Aspire.AppHost.Sdk/13.1.2)
  AppHost.cs                   ← Registers TimeTravel project as Aspire resource
```

## Agent Loop Pattern

`AgentOrchestrator` runs a stateless conversation loop:
1. Send `List<ChatMessage>` + tools to LLM via `IChatClient.GetResponseAsync()`
2. Extract `FunctionCallContent` (tool calls) and `TextContent` (reasoning)
3. If no tool calls → strip `<think>...</think>` tokens → return final text
4. Execute each tool, append `ChatRole.Tool` result messages
5. Repeat up to 15 iterations

## Configuration

`appsettings.json` values are overridden by `.env` (loaded manually in `Program.cs`):

```json
{
  "Agent": {
    "Provider": "lmstudio",
    "Model": "qwen3-coder-30b-a3b-instruct-mlx",
    "Endpoint": "http://localhost:1234/v1"
  },
  "Hub": {
    "ApiUrl": "https://<hub_url>",
    "ApiKey": "",
    "TaskName": "timetravel"
  },
  "Telemetry": {
    "Enabled": true,
    "OtlpEndpoint": "http://localhost:4317",
    "ServiceName": "TimeTravel",
    "EnableSensitiveData": true
  }
}
```

`.env` provides `Hub__ApiKey` and other overrides using `__` as config section separator.

## Key NuGet Packages

- `Microsoft.Agents.AI.OpenAI` (1.0.0-rc4) — agent framework + `AIFunctionFactory.Create()`
- `Microsoft.Extensions.Configuration.Binder` (10.0.5) — `Configuration.Bind()`
- `OpenTelemetry` + `OpenTelemetry.Exporter.OpenTelemetryProtocol` (1.*) — OTLP traces/metrics
- `Spectre.Console` (0.54.0) — rich console output

## API Request Format

```json
POST https://<hub_url>/verify
{
  "apikey": "<Hub__ApiKey>",
  "task": "timetravel",
  "answer": {
    "action": "help|configure|getConfig|reset",
    "param": "year|month|day|syncRatio|stabilization",
    "value": 1234
  }
}
```

API-configurable params (standby mode only): `day`, `month`, `year`, `syncRatio`, `stabilization`
Web UI only: PT-A switch, PT-B switch, PWR slider, standby/active toggle
Auto-changing (not settable): `internalMode`

## Runtime Requirements

- .NET 10 SDK
- LM Studio running at `http://localhost:1234/v1` with `qwen3-coder-30b-a3b-instruct-mlx` loaded
- Optional: OTEL Collector at `http://localhost:4317` for telemetry (Aspire provides this)

## Logging Tags

`RunLogger` writes structured entries with tags: `SESSION_START`, `LLM_REQUEST`, `LLM_RESPONSE`, `TOOL_CALL`, `TOOL_RESULT`, `TOOL_ERROR`, `API_REQUEST`, `API_RESPONSE_OK`, `API_RESPONSE_ERROR`, `NETWORK_ERROR`, `ERROR`, `INFO`
