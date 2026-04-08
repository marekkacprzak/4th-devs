# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Task Overview

**Task name:** `RadioMonitoring`

This agent intercepts and analyzes radio monitoring data from a listening station, then submits a final report about a hidden survivor city called "Syjon". The session has three phases:

1. **Start** — call `action: "start"` to initialize a listening session
2. **Listen** — repeatedly call `action: "listen"` to receive intercepted signals (text transcriptions or Base64-encoded binary files)
3. **Transmit** — call `action: "transmit"` with the final report (cityName, cityArea, warehousesCount, phoneNumber)

**API endpoint:** `POST https://<hub_url>/verify`
**API key:** stored in `.env` as `Hub__ApiKey`

The key engineering challenge is **smart data routing**: binary attachments can be very large; decode and analyze them locally (code) before sending to LLM. Use vision model for image files, text model for documents. Filter out noise without calling LLM at all.

## Commands

```bash
# Build
dotnet build

# Run the agent (reads .env or appsettings.json)
dotnet run --project RadioMonitoring

# Run with Aspire observability dashboard
dotnet run --project RadioMonitoring.AppHost

# Restore packages
dotnet restore
```

Environment variables are loaded from `.env` in the project root (key=value per line, `Hub__ApiKey`, `Hub__VerifyUrl`, etc.) — they override `appsettings.json`.

## Architecture

Follow the pattern from `../exam_04_01_task_agent/OkoEditor`. Two projects:

- **RadioMonitoring/** — main agent; minimal ASP.NET app (`Microsoft.NET.Sdk.Web`, net10.0) driving the agentic loop
- **RadioMonitoring.AppHost/** — .NET Aspire host for OpenTelemetry observability (OTLP to dashboard)

### Key service patterns (from reference project)

**AgentOrchestrator** — iterative LLM call loop (max ~15 iterations). Each iteration: call LLM → if `FunctionCallContent` tool calls returned, execute them, append `FunctionResultContent` messages, repeat. Strips `<think>…</think>` tokens before returning final text.

**OpenAiClientFactory** — wraps `OpenAIClient` for both `openai` and `lmstudio` providers. LM Studio uses `ApiKeyCredential("lm-studio")` with a custom endpoint URI. Optionally wraps with `UseOpenTelemetry()`.

**TelemetrySetup** — manually builds `TracerProvider` and `MeterProvider` via OpenTelemetry SDK, exporting to OTLP. Traces `Microsoft.Extensions.AI` and `Microsoft.Agents.AI` activity sources.

**RunLogger** — dual output: `ILogger` (structured) + plain text file (`logs/run-{timestamp}.log`) for easy post-run analysis.

**Tools** — plain methods decorated with `[Description]` attributes, registered via `AIFunctionFactory.Create(...)`.

### RadioMonitoring-specific routing pipeline

The agent needs a programmatic router (not LLM) that processes each `listen` response:

1. If `transcription` field present → analyze text for relevance, send relevant fragments to LLM
2. If `attachment` (Base64) present → decode binary locally:
   - Detect MIME type from `meta` field
   - Images (`image/*`) → send to Vision model (qwen3-vl-8b)
   - PDF/text documents → extract text locally, send to Agent model
   - Unknown/noise → discard without LLM call
3. Stop when API signals no more data

### Configuration structure (`appsettings.json`)

```json
{
  "Agent": {
    "Provider": "lmstudio",
    "Model": "qwen3-coder-30b-a3b-instruct-mlx",
    "Endpoint": "http://localhost:1234/v1"
  },
  "Vision": {
    "Provider": "lmstudio",
    "Model": "qwen/qwen3-vl-8b",
    "Endpoint": "http://localhost:1234/v1"
  },
  "Hub": {
    "ApiKey": "",
    "VerifyUrl": "https://<hub_url>/verify",
    "TaskName": "radiomonitoring"
  },
  "Telemetry": {
    "Enabled": true,
    "OtlpEndpoint": "http://localhost:4317",
    "ServiceName": "RadioMonitoring",
    "EnableSensitiveData": true
  }
}
```

Values in `.env` override the above using `__` as section separator (e.g. `Hub__ApiKey=...`).

### API request/response format

```json
POST /verify
{
  "apikey": "<Hub__ApiKey>",
  "task": "radiomonitoring",
  "answer": { "action": "start" | "listen" | "transmit" }
}
```

Transmit payload:
```json
{
  "action": "transmit",
  "cityName": "NazwaMiasta",
  "cityArea": "12.34",
  "warehousesCount": 321,
  "phoneNumber": "123456789"
}
```

`cityArea` must be a string with exactly 2 decimal places (true mathematical rounding).

## Custom Commands

- `/plan` — systematic planning: problem analysis → options → user approval → saved plan in `./details/YYYY/MM/DD-topic-plan.md`
- `/implement` — step-by-step implementation with task tracking and mandatory updates to `TODO.md` and `CHANGELOG.md`

## Prerequisites

- .NET 10 SDK
- LM Studio running at `http://localhost:1234/v1` with:
  - `qwen3-coder-30b-a3b-instruct-mlx` (agent/text model)
  - `qwen/qwen3-vl-8b` (vision model)
- `.env` file in project root with `Hub__ApiKey` set
