# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Task Overview

**Task name:** `OkoEditor`

This agent modifies incident records in the OKO (Centrum Operacyjne) surveillance system via API only — never via the web UI. Three required changes:

1. Change the Skolwin city report classification from vehicles/people → **animals**
2. Find the Skolwin task and mark it as done, noting animals (e.g., beavers) were spotted
3. Create a new incident report about human movement near the unpopulated city **Komarowo**
4. Execute action `done` to confirm all changes

**OKO Web UI (read-only for exploration):** `https://<oko_url>/` — login: `Zofia` / `Zofia2026!`
**API endpoint:** `/verify` at the Centrala base URL
**API key:** your personal `apikey`

## Commands

```bash
# Build
dotnet build

# Run the agent (reads .env or appsettings.json)
dotnet run --project OkoEditor

# Run with Aspire observability dashboard
dotnet run --project OkoEditor.AppHost

# Restore packages
dotnet restore
```

Environment variables can be loaded from a `.env` file in the project root (key=value per line). They override `appsettings.json`.

## Architecture

This project follows the pattern established in `../exam_01_03_task_agent/ProxyAgent`. Two projects:

- **OkoEditor/** — main agent; a minimal ASP.NET or console app that drives the agentic loop
- **OkoEditor.AppHost/** — .NET Aspire host for OpenTelemetry observability (OTLP to dashboard)

### Key patterns from the reference project

**Agent loop** (`AgentOrchestrator`): Iterative LLM call loop (max ~5 iterations). On each iteration, executes any `FunctionCallContent` tool calls returned by the model, appends `FunctionResultContent` messages, and continues until no tool calls remain — then returns the final `TextContent`. Strips `<think>…</think>` tokens from responses.

**LLM client** (`OpenAiClientFactory`): Wraps `OpenAIClient` to support both `openai` and `lmstudio` providers. LM Studio uses `ApiKeyCredential("lm-studio")` with a custom endpoint URI. Optionally wraps the client with `UseOpenTelemetry()` middleware.

**Telemetry** (`TelemetrySetup`): Manually builds `TracerProvider` and `MeterProvider` via the OpenTelemetry SDK, exporting to OTLP. Traces `Microsoft.Extensions.AI` and `Microsoft.Agents.AI` activity sources.

**Tools**: Defined as plain methods decorated with `[Description]` attributes and registered via `AIFunctionFactory.Create(...)`. The OkoEditor agent needs tools for each API action: read/explore, modify classification, update task, create incident, submit done.

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
    "ApiKey": "<your-apikey>"
  },
  "Telemetry": {
    "Enabled": true,
    "ServiceName": "OkoEditor",
    "OtlpEndpoint": "http://localhost:4317"
  }
}
```

### OKO API format

```json
POST /verify
{
  "apikey": "<your-apikey>",
  "task": "okoeditor",
  "answer": {
    "action": "help"
  }
}
```

Start with `action: "help"` to discover all available actions. End with `action: "done"` once all three modifications are complete.

## Custom Commands

- `/plan` — systematic planning: problem analysis → options → user approval → saved plan in `./details/YYYY/MM/DD-topic-plan.md`
- `/implement` — step-by-step implementation with task tracking and mandatory updates to `TODO.md` and `CHANGELOG.md`

## Prerequisites

- .NET 10 SDK
- LM Studio running at `http://localhost:1234/v1` with model `qwen3-coder-30b-a3b-instruct-mlx`
- `OTEL_EXPORTER_OTLP_ENDPOINT` env var or `Telemetry:OtlpEndpoint` in config if using Aspire telemetry
