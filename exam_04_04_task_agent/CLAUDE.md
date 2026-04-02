# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Task Overview

**Task name:** `filesystem`

This agent parses Natan's trade notes (a survivalist's records from post-apocalyptic cities) and organizes them into a virtual file system via API. The goal is to create three directories with structured data:

- `/miasta/[city_name]` — JSON file with goods the city needs and quantities (no Polish chars, no units)
- `/osoby/[person_file]` — Name + markdown link to the city they manage
- `/towary/[good_name]` — Markdown link to the city offering that good (singular nominative form)

**Notes source:** `https://<hub_url>/dane/natan_notes.zip`
**Filesystem preview:** `https://<hub_url>/filesystem_preview.html`
**API endpoint:** `POST /verify` at the Centrala base URL

## Commands

```bash
# Build
dotnet build

# Run the agent (reads .env or appsettings.json)
dotnet run --project Filesystem

# Run with Aspire observability dashboard
dotnet run --project Filesystem.AppHost

# Restore packages
dotnet restore
```

Environment variables are loaded from a `.env` file in the project root (key=value per line). They override `appsettings.json`.

## Architecture

Two projects following the pattern from `../exam_04_01_task_agent`:

- **Filesystem/** — main agent; console app driving the agentic loop
- **Filesystem.AppHost/** — .NET Aspire host for OpenTelemetry observability (OTLP to dashboard)

### Key patterns

**Agent loop** (`AgentOrchestrator`): Iterative LLM call loop (max ~15 iterations). Executes `FunctionCallContent` tool calls, appends `FunctionResultContent` messages, repeats until no tool calls remain. Strips `<think>…</think>` tokens.

**LLM client** (`OpenAiClientFactory`): Wraps `OpenAIClient` for both `openai` and `lmstudio` providers. LM Studio uses `ApiKeyCredential("lm-studio")` with custom endpoint. Optionally wraps with `UseOpenTelemetry()` middleware.

**Telemetry** (`TelemetrySetup`): `TracerProvider` + `MeterProvider` via OpenTelemetry SDK, exporting to OTLP. Traces `Microsoft.Extensions.AI` and `Microsoft.Agents.AI` sources.

**Tools**: Plain methods with `[Description]` attributes, registered via `AIFunctionFactory.Create(...)`. Required tools: download/parse notes, create directory, create file, list directory, reset, done.

**Logging** (`RunLogger`): Structured file logging to `Filesystem/logs/YYYY-MM-DD_HH-mm-ss.log`. Tagged entries: `LLM_REQUEST`, `TOOL_CALL`, `API_REQUEST`, `API_RESPONSE_OK/ERROR`, etc.

### Configuration structure

```json
{
  "Agent": {
    "Provider": "lmstudio",
    "Model": "qwen3-coder-30b-a3b-instruct-mlx",
    "Endpoint": "http://localhost:1234/v1"
  },
  "Hub": {
    "ApiUrl": "<centrala-base-url>",
    "ApiKey": "<your-apikey>",
    "TaskName": "filesystem"
  },
  "Telemetry": {
    "Enabled": true,
    "ServiceName": "Filesystem",
    "OtlpEndpoint": "http://localhost:4317"
  }
}
```

### Filesystem API format

Single operation:
```json
POST /verify
{
  "apikey": "<your-apikey>",
  "task": "filesystem",
  "answer": {
    "action": "createFile",
    "path": "/miasta/warszawa",
    "content": "{\"chleb\": 5}"
  }
}
```

Batch mode (preferred — builds the entire filesystem in one request):
```json
{
  "apikey": "<your-apikey>",
  "task": "filesystem",
  "answer": [
    { "action": "createDir", "path": "/miasta" },
    { "action": "createFile", "path": "/miasta/warszawa", "content": "..." }
  ]
}
```

Special actions: `help`, `reset` (clears all files), `done` (submits for verification).

### File content requirements

- **`/miasta/[city]`**: JSON with goods needed and quantities as numbers, no Polish characters anywhere, no units
- **`/osoby/[person]`**: First name + last name, plus `[City Name](../miasta/city_name)` markdown link
- **`/towary/[good]`**: `[City Name](../miasta/city_name)` markdown link; good name is singular nominative (e.g., `koparka` not `koparki`)
- No Polish characters (`ą ę ó ś ź ż ć ń ł`) in any file names or JSON content

## Custom Commands

- `/plan` — systematic planning: problem analysis → options → user approval → saved plan in `./details/YYYY/MM/DD-topic-plan.md`
- `/implement` — step-by-step implementation with task tracking and mandatory updates to `TODO.md` and `CHANGELOG.md`

## Prerequisites

- .NET 10 SDK
- LM Studio running at `http://localhost:1234/v1` with model `qwen3-coder-30b-a3b-instruct-mlx`
- `OTEL_EXPORTER_OTLP_ENDPOINT` env var or `Telemetry:OtlpEndpoint` in config if using Aspire telemetry
