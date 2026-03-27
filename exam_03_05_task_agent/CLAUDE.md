# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

C# AI agent ("savethem") built with Microsoft Agent Framework. The agent dynamically discovers tools via a tool search API, retrieves a 10x10 terrain map and vehicle data, then uses BFS pathfinding to plan the optimal route to city "Skolwin" within 10 food rations and 10 fuel units.

**Reference implementation:** `/Users/slipper/Nauka/4th-devs/exam_03_03_task_agent/` (ReactorAgent)

## Commands

```bash
# Build
dotnet build

# Run the agent (requires SavethemAgent/.env with Hub__ApiKey)
dotnet run --project SavethemAgent/

# Run with Aspire observability dashboard
dotnet run --project SavethemAgent.AppHost/
```

**Setup before running:**
```bash
cp SavethemAgent/.env.example SavethemAgent/.env
# Edit .env and fill in Hub__ApiKey
```

## Architecture

```
savethem.slnx
├── SavethemAgent/
│   ├── Program.cs              # 7-phase orchestration + deterministic fallback
│   ├── Config/                 # AgentConfig, HubConfig, TelemetryConfig
│   ├── Models/                 # GridMap (10x10), Vehicle, RouteResult
│   ├── Services/               # HubApiClient, RequestLogger, MapParser, VehicleParser, RoutePlanner
│   ├── Tools/                  # SavethemTools — 6 AI functions
│   ├── Adapters/               # OpenAiClientFactory, LoggingChatClient
│   ├── Telemetry/              # TelemetrySetup (OTLP)
│   ├── UI/                     # ConsoleUI (Spectre.Console)
│   ├── appsettings.json        # Non-secret config (Agent, Telemetry, Hub.MaxRetries)
│   └── .env.example            # Template for secrets (Hub__ApiKey, URLs)
└── SavethemAgent.AppHost/      # Aspire host for observability
```

### Agent Flow

```
SearchAvailableTools() → CallTool() → ParseAndStoreMap() →
ParseAndStoreVehicles() → PlanOptimalRoute() → SubmitSolution()
```

1. LLM agent discovers tools via `toolsearch` API (queries in English)
2. Calls discovered tool URLs to get map and vehicle data
3. `ParseAndStoreMap` / `ParseAndStoreVehicles` parse and cache the data
4. `PlanOptimalRoute` runs BFS + picks vehicle with best resource margin
5. `SubmitSolution` POSTs to `/verify`

### Key Constraints

- 10x10 grid with obstacles (rivers, trees, stones)
- 10 food rations — 1 per step regardless of vehicle
- 10 fuel units — `vehicle.FuelPerStep` per step; walking = 0 fuel
- All tool queries must be in English

## Configuration

**Hub credentials in `SavethemAgent/.env`** (not committed):
```
Hub__ApiKey=your-api-key-here
Hub__ToolSearchUrl=https://<hub_api>/api/toolsearch
Hub__VerifyUrl=https://<hub_api>/verify
Hub__TaskName=savethem
```

`appsettings.json` contains only non-secret settings (retry policy, Agent LLM config, Telemetry).

## Logging

- HTTP logs: `logs/savethem_run_YYYYMMDD_HHmmss.log` (`RequestLogger`)
- LLM logs: `logs/savethem_agent_YYYYMMDD_HHmmss.log` (`LoggingChatClient`)

## API Reference

```
Tool Search:  POST https://<hub_api>/api/toolsearch
              Body: { "apikey": "...", "query": "..." }
              Response: top 3 matching tools as JSON

Any tool:     POST <tool_url>
              Body: { "apikey": "...", "query": "..." }
              Response: JSON with 3 best-matching results

Verify:       POST https://<hub_api>/verify
              Body: { "apikey": "...", "task": "savethem", "answer": ["vehicle_name", "right", ...] }

Preview:      https://<hub_api>/savethem_preview.html
```
