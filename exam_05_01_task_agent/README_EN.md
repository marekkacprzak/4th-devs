# RadioMonitoring — Radio Signal Intelligence Agent

A C# agent that intercepts and analyzes radio monitoring data to locate a hidden survivor city called "Syjon", then transmits a final intelligence report to headquarters.

## Overview

The agent implements a two-phase hybrid pipeline:

1. **Phase 1 — Programmatic Data Collection**: Calls the radio monitoring API to start a session, then loops listening for signals. Each signal is routed by type:
   - **Text transcriptions** — noise-filtered, meaningful content accumulated
   - **Image attachments** (Base64) — decoded and analyzed by a Vision LLM model
   - **Text/JSON documents** (Base64) — decoded to UTF-8 and accumulated
   - **Unknown binary** — logged and discarded without LLM calls

2. **Phase 2 — Agentic Analysis**: The collected intelligence is fed to an `AgentOrchestrator` (Microsoft Agent Framework loop). The LLM analyzes all data, extracts the four required fields, and calls `TransmitReport`.

## Architecture

```
RadioMonitoring/               — Main agent project (ASP.NET Web, net10.0)
  Program.cs                   — Entry point: .env loading, config, two-phase flow
  Config/                      — AgentConfig, VisionConfig, HubConfig, TelemetryConfig
  Adapters/OpenAiClientFactory — Creates IChatClient for lmstudio/openai providers
  Services/
    AgentOrchestrator          — Microsoft.Extensions.AI agentic loop (max 15 iterations)
    CentralaApiClient          — HTTP POST to /verify with retry, rate limiting, tracing
    RadioCollector             — Phase 1 pipeline: start → listen loop → smart routing
    RunLogger                  — Timestamped file logger (logs/ directory)
  Tools/RadioTools             — TransmitReport tool registered via AIFunctionFactory
  Models/ListenResponse        — DTO for listen API responses
  Telemetry/TelemetrySetup     — OpenTelemetry TracerProvider + MeterProvider (OTLP)
  UI/ConsoleUI                 — Spectre.Console rich terminal output

RadioMonitoring.AppHost/       — .NET Aspire host for observability dashboard
```

## Prerequisites

- **.NET 10 SDK**
- **LM Studio** running at `http://localhost:1234/v1` with:
  - `qwen3-coder-30b-a3b-instruct-mlx` — agent/text analysis model
  - `qwen/qwen3-vl-8b` — vision model for image analysis

## Configuration

Create a `.env` file in the `RadioMonitoring/` directory (or the working directory when running):

```
Hub__VerifyUrl=https://<hub_url>/verify
Hub__ApiKey=your-api-key-here
```

All other settings have defaults in `appsettings.json`. Key sections:

```json
{
  "Agent":  { "Provider": "lmstudio", "Model": "qwen3-coder-30b-a3b-instruct-mlx" },
  "Vision": { "Provider": "lmstudio", "Model": "qwen/qwen3-vl-8b" },
  "Hub":    { "VerifyUrl": "", "ApiKey": "", "TaskName": "radiomonitoring" }
}
```

## Running

```bash
# Restore packages
dotnet restore

# Run the agent directly
dotnet run --project RadioMonitoring

# Run with Aspire observability dashboard (telemetry)
dotnet run --project RadioMonitoring.AppHost
```

## Output

- **Console** — Rich terminal output via Spectre.Console showing each phase, LLM calls, tool invocations, and the final API response
- **Log file** — Full structured log at `logs/YYYY-MM-DD_HH-mm-ss.log` with all requests and responses untruncated
- **Telemetry** — OpenTelemetry traces and metrics exported via OTLP (visible in Aspire dashboard when using AppHost)

## API Protocol

```
POST https://<hub_url>/verify

Start:    { "action": "start" }
Listen:   { "action": "listen" }
Transmit: { "action": "transmit", "cityName": "...", "cityArea": "12.34",
            "warehousesCount": 5, "phoneNumber": "123456789" }
```

The agent finds: **cityName** (real name of "Syjon"), **cityArea** (km², 2 decimal places), **warehousesCount** (integer), **phoneNumber** (digits only).
