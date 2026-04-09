# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Task Overview

**Task name:** `goingthere`

Navigate a rocket through a 3×12 grid to reach the base in column 12 without hitting rocks or being shot down by OKO radar traps.

**Game loop per move:**
1. Check frequency scanner — is there an active OKO radar trap?
2. If trapped: parse the (possibly corrupted) scanner response, extract `frequency` and `detectionCode`, compute `SHA1(detectionCode + "disarm")`, POST to disarm endpoint
3. Fetch radio hint from `getmessage` — describes where the rock is in the next column (left/right/straight, may use nautical language)
4. Send movement command: `go` (straight), `left` (up + forward), `right` (down + forward)
5. Repeat until column 12 — then receive the flag

**Key resilience requirements:**
- Scanner responses are intentionally corrupted/malformed JSON — must extract fields with fuzzy parsing (regex, not strict JSON)
- API may randomly return errors even on valid requests — retry on failure

**API endpoints (base: `https://<hub_url>`):**
- `POST /verify` — game control (`start`, `go`, `left`, `right`)
- `GET /api/frequencyScanner?key=<apikey>` — check for radar trap
- `POST /api/frequencyScanner` — disarm radar trap
- `POST /api/getmessage` — get rock position hint for next column

## Commands

```bash
# Build
dotnet build

# Run the agent
dotnet run --project GoingThere

# Run with Aspire observability dashboard
dotnet run --project GoingThere.AppHost

# Restore packages
dotnet restore
```

Environment variables are loaded from `.env` in the project root (key=value per line), overriding `appsettings.json`.

## Architecture

Follow the pattern from `../exam_04_01_task_agent`. Expected project structure:

- **GoingThere/** — main agent console app
  - `Program.cs` — config loading, DI, validation, agent start
  - `Services/AgentOrchestrator.cs` — iterative LLM tool-call loop (max ~15 iterations)
  - `Services/HubApiClient.cs` — game API + frequency scanner calls with retry/backoff
  - `Services/RunLogger.cs` — timestamped file logger (`logs/YYYY-MM-DD_HH-mm-ss.log`)
  - `Tools/GameTools.cs` — AIFunction tools registered via `AIFunctionFactory.Create`
  - `Adapters/OpenAiClientFactory.cs` — supports `lmstudio` and `openai` providers
  - `Telemetry/TelemetrySetup.cs` — OTLP trace/metric export
  - `Config/AgentConfig.cs` — typed config classes
  - `UI/ConsoleUI.cs` — Spectre.Console rich output
- **GoingThere.AppHost/** — .NET Aspire host for OpenTelemetry observability

### Key patterns from exam_04_01_task_agent

**Agent loop** (`AgentOrchestrator`): Each iteration sends messages + tools to LLM, executes returned `FunctionCallContent` tool calls, appends `FunctionResultContent`, loops until no more tool calls. Strips `<think>…</think>` tokens from final response.

**LLM client** (`OpenAiClientFactory`): `lmstudio` provider uses `ApiKeyCredential("lm-studio")` with custom endpoint URI. Optionally wraps with `UseOpenTelemetry()`.

**Telemetry** (`TelemetrySetup`): Manually builds `TracerProvider`/`MeterProvider` via OTLP. Traces `Microsoft.Extensions.AI` and `Microsoft.Agents.AI` activity sources.

**Tools**: Plain methods with `[Description]` attributes, registered via `AIFunctionFactory.Create(...)`.

**Corrupted JSON parsing**: Use regex to extract `frequency` and `detectionCode` from scanner responses rather than `JsonSerializer.Deserialize`.

### Configuration structure

```json
{
  "Agent": {
    "Provider": "lmstudio",
    "Model": "qwen3-coder-30b-a3b-instruct-mlx",
    "Endpoint": "http://localhost:1234/v1"
  },
  "Hub": {
    "ApiUrl": "https://<hub_url>",
    "ApiKey": "<your-apikey>"
  },
  "Telemetry": {
    "Enabled": true,
    "ServiceName": "GoingThere",
    "OtlpEndpoint": "http://localhost:4317"
  }
}
```

### Game API format

```json
POST /verify
{
  "apikey": "<your-apikey>",
  "task": "goingthere",
  "answer": { "command": "start" }
}
```

Start response includes current position, target row in column 12, and current column layout (free rows + rock position). Use `go`/`left`/`right` as the `command` value for subsequent moves.

### Radar disarm flow

```csharp
// GET /api/frequencyScanner?key=<apikey>
// If response contains "It's clear!" — safe to move
// Otherwise, parse corrupted JSON with regex:
var frequency = ExtractField(response, "frequency");
var detectionCode = ExtractField(response, "detectionCode");
var disarmHash = SHA1(detectionCode + "disarm");

// POST /api/frequencyScanner
{ "apikey": "...", "frequency": 123, "disarmHash": "abc123..." }
```

## Custom Commands

- `/plan` — systematic planning: problem analysis → options → user approval → saved plan in `./details/YYYY/MM/DD-topic-plan.md`
- `/implement` — step-by-step implementation with task tracking and mandatory updates to `TODO.md` and `CHANGELOG.md`

## Prerequisites

- .NET 10 SDK
- LM Studio running at `http://localhost:1234/v1` with model `qwen3-coder-30b-a3b-instruct-mlx`
- `OTEL_EXPORTER_OTLP_ENDPOINT` env var or `Telemetry:OtlpEndpoint` in config if using Aspire telemetry
