# SavethemAgent

A C# AI agent built with Microsoft Agent Framework that plans an optimal route across a 10x10 terrain map to reach city Skolwin, managing limited resources (10 food rations, 10 fuel units).

## Task

The agent dynamically discovers available tools via a tool search API, retrieves the terrain map and vehicle data, then uses BFS pathfinding with resource optimization to find the optimal route. The result is submitted to a verification endpoint.

## Architecture

```
SearchAvailableTools() → CallTool() → ParseAndStoreMap() →
ParseAndStoreVehicles() → PlanOptimalRoute() → SubmitSolution()
```

**Key components:**

| Component | Description |
|-----------|-------------|
| `Tools/SavethemTools.cs` | 6 AI functions exposed to the LLM agent |
| `Services/HubApiClient.cs` | HTTP client with retry, rate limiting, request logging |
| `Services/RequestLogger.cs` | File-based HTTP request/response logger |
| `Services/MapParser.cs` | Flexible JSON→GridMap parser (multiple API shapes) |
| `Services/RoutePlanner.cs` | BFS pathfinding + resource constraint optimizer |
| `Services/VehicleParser.cs` | Flexible JSON→Vehicle list parser |
| `Adapters/LoggingChatClient.cs` | LLM request/response file logger |
| `Telemetry/TelemetrySetup.cs` | OpenTelemetry OTLP tracing and metrics |

## Setup

1. Copy `.env.example` to `.env` in the `SavethemAgent/` directory:
   ```bash
   cp SavethemAgent/.env.example SavethemAgent/.env
   ```

2. Fill in your API key in `.env`:
   ```
   Hub__ApiKey=your-api-key-here
   Hub__ToolSearchUrl=https://<hub_api>/api/toolsearch
   Hub__VerifyUrl=https://<hub_api>/verify
   Hub__TaskName=savethem
   ```

3. Make sure LM Studio is running with the model `qwen3-coder-30b-a3b-instruct-mlx` on `http://localhost:1234`.

## Running

**Direct (no Aspire):**
```bash
dotnet run --project SavethemAgent/
```

**With Aspire observability dashboard:**
```bash
dotnet run --project SavethemAgent.AppHost/
```

## Logs

All HTTP requests/responses are logged to:
```
SavethemAgent/bin/Debug/net10.0/logs/savethem_run_YYYYMMDD_HHmmss.log
```

All LLM interactions are logged to:
```
SavethemAgent/bin/Debug/net10.0/logs/savethem_agent_YYYYMMDD_HHmmss.log
```

## Configuration

`appsettings.json` controls non-secret settings. Hub credentials are loaded from `.env`:

| Key | Description |
|-----|-------------|
| `Hub__ApiKey` | API key for <hub_api> |
| `Hub__ToolSearchUrl` | Tool search endpoint |
| `Hub__VerifyUrl` | Route verification endpoint |
| `Hub__TaskName` | Task identifier (`savethem`) |
| `Agent__Model` | LLM model name |
| `Agent__Endpoint` | LLM endpoint URL |

## Resource Constraints

- **10 food rations** — consumed per step (every move costs 1 food)
- **10 fuel units** — consumed per step based on vehicle (`FuelPerStep`)
- Walking (`on_foot`) costs 0 fuel but still consumes food
- The planner selects the vehicle with the best safety margin within constraints
