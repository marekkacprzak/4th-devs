# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Task Overview

This is a C# .NET agent that detects anomalies in ~10,000 sensor JSON files from a power plant system and submits flagged IDs to the Hub API. Task name: `evaluation`.

**Anomaly types to detect:**
- Measurement values outside valid ranges
- Operator notes claiming "OK" but data is invalid
- Operator notes claiming "errors found" but data is valid
- Sensors returning data they shouldn't (wrong sensor type)

**Valid ranges for active sensors:**
- `temperature_K`: 553–873
- `pressure_bar`: 60–160
- `water_level_meters`: 5.0–15.0
- `voltage_supply_v`: 229.0–231.0
- `humidity_percent`: 40.0–80.0

**Data source:** `<SensorsZipUrl>`

**Submission format:**
```json
{ "apikey": "...", "task": "evaluation", "answer": { "recheck": ["0001","0002",...] } }
```

## Commands

```bash
dotnet build                       # Build solution
dotnet run                         # Run main agent project
dotnet run --project *.AppHost     # Run with Aspire observability
dotnet restore                     # Restore packages
dotnet clean                       # Clean build artifacts
```

## Architecture

### Technology Stack
- **Language/Runtime:** C# 10+, .NET 10.0
- **Agent Framework:** Microsoft.Agents.AI.OpenAI
- **Observability:** .NET Aspire + OpenTelemetry (OTLP)
- **LLM Backend:** LM Studio local instance at `http://localhost:1234/v1`
  - Agent model: `qwen3-coder-30b-a3b-instruct-mlx`
- **UI:** Spectre.Console

### Project Layout Convention
```
<ProjectName>/              # Main agent (e.g., EvaluationAgent)
│   Program.cs
│   appsettings.json
│   Config/                 # AgentConfig, HubConfig, TelemetryConfig
│   Services/               # HubApiClient, data downloader, domain logic
│   Tools/                  # Agent tool definitions
│   Adapters/               # OpenAiClientFactory (LM Studio adapter)
│   Telemetry/              # OpenTelemetry setup
│   └── UI/                 # ConsoleUI (Spectre.Console helpers)
<ProjectName>.AppHost/      # Aspire orchestration host
```

### Environment Variables
- `Hub__ApiUrl` — Hub central API endpoint
- `Hub__DataBaseUrl` — Hub database URL
- `Hub__ApiKey` — Authentication key
- `OPENAI_API_KEY` — Set to `"lm-studio"` for local use
- `OTEL_EXPORTER_OTLP_ENDPOINT` — Telemetry collector (default: `http://localhost:4317`)

### Key Design Decisions

**Cost optimization is critical** — 10,000 files cannot be sent to an LLM directly:
1. **Programmatic first:** Validate value ranges without LLM — catches most anomalies for free
2. **Deduplicate:** Many readings are identical; cache LLM responses by content hash
3. **Batch:** Group similar entries and classify in batches to reduce output tokens
4. **LLM only for:** Operator note validation (is "OK" claim consistent with the data?)

**HubApiClient** must implement rate-limit handling (respect `retry-after` headers) and exponential backoff retry logic.

**Aspire AppHost** wires up OpenTelemetry; run via `*.AppHost` project to get tracing in the local dashboard.

## Custom Commands

- `/plan` — Systematic planning: problem analysis → options → user consultation → documented plan
- `/implement` — Execute a previously created plan with incremental verification and rollback on errors

## Reference Implementation

`exam_02_03_task_agent` (FailureAgent) is a complete working example with the same stack. Use it as reference for: HubApiClient with rate limiting, OpenTelemetry setup, Spectre.Console UI patterns, and LLM condensation strategy.
