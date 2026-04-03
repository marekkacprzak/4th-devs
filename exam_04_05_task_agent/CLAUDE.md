# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Task Overview

**Task name:** `foodwarehouse`
**Goal:** Build a C# agent using Microsoft Agent Framework that automates food warehouse order management. The agent must:
1. Fetch city demand data from `https://<hub_url>/dane/food4cities.json`
2. Query a SQLite database (read-only) via API to get `destination` codes, `creatorID`, and data for SHA1 signature generation
3. Create one order per city with correct `creatorID`, `destination`, and `signature`
4. Append exact items per city (no excess, no deficit)
5. Call `done` to finalize and receive a flag

**Verify endpoint:** `https://<hub_url>/verify`
**API key:** stored in `.env` as `Hub__ApiKey`

## Reference Project

Base this implementation on `../exam_04_01_task_agent` (OkoEditor). Mirror its architecture exactly, replacing OKO-specific tools with FoodWarehouse tools.

## Project Structure to Build

```
FoodWareHouse/                  # Main agent project
  Program.cs                    # Entry point, DI setup, agent orchestration
  FoodWareHouse.csproj
  Config/
    AgentConfig.cs              # LLM provider/model/endpoint
    HubConfig.cs                # API key, verify URL
    TelemetryConfig.cs
  Services/
    AgentOrchestrator.cs        # Core agentic loop (max ~15 iterations)
    CentralaApiClient.cs        # HTTP client for /verify with retry logic
    RunLogger.cs                # File logger → logs/YYYY-MM-DD_HH-mm-ss.log
  Tools/
    FoodWareHouseTools.cs       # AI tools: CallVerifyApi, FetchCityDemands
  Adapters/
    OpenAiClientFactory.cs      # Creates IChatClient for lmstudio or openai
  Telemetry/
    TelemetrySetup.cs           # OpenTelemetry TracerProvider + MeterProvider
  UI/
    ConsoleUI.cs                # Spectre.Console rich output
  Models/
    VerifyRequest.cs            # DTO for /verify requests
  appsettings.json

FoodWareHouse.AppHost/          # Aspire orchestrator
  AppHost.cs
  FoodWareHouse.AppHost.csproj
```

## Build & Run

```bash
# Run via Aspire (with dashboard + telemetry)
cd FoodWareHouse.AppHost
dotnet run

# Run agent directly
cd FoodWareHouse
dotnet run

# Build only
dotnet build

# Reset task state if something breaks
# POST to /verify with { "tool": "reset" }
```

## Key Configuration (appsettings.json)

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
    "TaskName": "foodwarehouse",
    "MaxRetries": 3,
    "RetryDelayMs": 1000
  },
  "Telemetry": {
    "Enabled": true,
    "OtlpEndpoint": "http://localhost:4317",
    "ServiceName": "FoodWareHouse",
    "EnableSensitiveData": true
  }
}
```

Environment overrides from `.env` file take precedence (already contains `Hub__ApiKey`, `Hub__VerifyUrl`, `Hub__ApiUrl`).

## Architecture

**Agent loop** (`AgentOrchestrator`): maintains message history, calls LLM → extracts tool calls → executes tools → appends results → repeats until no tool calls or max iterations.

**Tools** exposed to LLM:
- `CallVerifyApi(tool, action?, query?, additionalFieldsJson?)` — generic wrapper for all `/verify` API calls (database queries, order CRUD, signatureGenerator, done)
- `FetchCityDemands()` — fetches `food4cities.json` and returns parsed demand data

**Signature generation:** use the `signatureGenerator` tool via the API. The agent must first query the SQLite database to find the correct user data, then call `signatureGenerator` with that data to get the SHA1 signature needed for order creation.

**File logging** (`RunLogger`): writes timestamped entries to `logs/YYYY-MM-DD_HH-mm-ss.log`. Tags: `SESSION_START`, `LLM_REQUEST`, `LLM_RESPONSE`, `TOOL_CALL`, `TOOL_RESULT`, `TOOL_ERROR`, `API_REQUEST`, `API_RESPONSE_OK`, `API_RESPONSE_ERROR`, `ERROR`, `INFO`.

**Telemetry**: OpenTelemetry exported to OTLP (Aspire dashboard). Activity sources: `FoodWareHouse`, `FoodWareHouse.Centrala`, `Microsoft.Extensions.AI`, `Microsoft.Agents.AI`.

## Key NuGet Packages

- `Microsoft.Agents.AI.OpenAI` v1.0.0-rc4 — agent framework
- `Aspire.AppHost.Sdk` — Aspire orchestration
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` — OTLP export
- `Serilog.Sinks.File` or manual file writes via `RunLogger`
- `Spectre.Console` — rich console UI
- `dotenv.net` — `.env` file loading

## Custom Slash Commands

- `/plan <topic>` — systematic planning with multi-option analysis, saves to `./details/YYYY/MM/DD-[topic]-plan.md`
- `/implement` — step-by-step implementation following the latest saved plan, updates `TODO.md` and `CHANGELOG.md`

## FoodWarehouse API Reference

| Tool | Action/Query | Purpose |
|------|-------------|---------|
| `help` | — | Get API documentation |
| `database` | `show tables` | List SQLite tables |
| `database` | `select * from X` | Query SQLite (read-only) |
| `signatureGenerator` | — | Generate SHA1 signature from user data |
| `orders` | `get` | List current orders |
| `orders` | `create` | Create order with title, creatorID, destination, signature |
| `orders` | `append` | Add items (single or batch) to order |
| `reset` | — | Restore initial task state |
| `done` | — | Final verification, returns flag |
