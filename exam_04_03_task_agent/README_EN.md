# Domatowo — Rescue Mission Agent

AI Devs 4 exam task. A C# agent that plays a map-based rescue mission: locate a survivor hiding in the bombed city of Domatowo and coordinate their evacuation.

## Mission Brief

A human survivor is broadcasting a radio signal from somewhere inside Domatowo — a destroyed city on an 11×11 grid. The intercepted message says they are hiding in **one of the tallest buildings**, are armed and injured. The agent must:

1. Retrieve and analyze the city map
2. Identify tall-building candidates
3. Deploy transporters and scouts efficiently within a **300 action-point budget**
4. Inspect candidate buildings until the survivor is confirmed
5. Call a rescue helicopter to the survivor's exact location

## Architecture

```
Domatowo/                         # Main agent project (.NET 10)
├── Program.cs                    # Entry point, .env loading, manual service wiring
├── appsettings.json              # Agent / Vision / Hub / Telemetry config
├── Adapters/OpenAiClientFactory  # IChatClient factory (lmstudio / openai)
├── Config/                       # AgentConfig, HubConfig, TelemetryConfig POCOs
├── Models/DomatowoModels         # MapCell, CityMap, UnitState, Budget, RouteStep
├── Services/
│   ├── DomatowoOrchestrator      # C#-driven 5-phase state machine (CORE)
│   ├── CentralaApiClient         # HTTP POST with retry + rate-limit + OTLP tracing
│   └── RunLogger                 # Structured file logger (logs/*.log)
├── Telemetry/TelemetrySetup      # TracerProvider + MeterProvider → OTLP
├── Tools/DomatowoTools           # LLM-callable API action wrappers
├── UI/ConsoleUI                  # Spectre.Console rich terminal output
└── logs/                         # Runtime log files

Domatowo.AppHost/                 # .NET Aspire observability host (dashboard :5010)
```

## Action Costs

| Action | Cost |
|--------|------|
| Create scout | 5 pts |
| Create transporter + N scouts | 5 + 5×N pts |
| Move transporter | 1 pt / field (roads only) |
| Move scout | 7 pts / field (any terrain) |
| Inspect field | 1 pt |
| Deploy scouts | 0 pts |
| Call helicopter | 0 pts |

**Strategy**: Transporters carry scouts to drop-off points near tall buildings. Scouts walk only the last 1–2 fields on foot, minimizing expensive scout movement.

## Orchestrator Phases

| Phase | Name | Description |
|-------|------|-------------|
| 0 | Discovery | `help` + `getMap` — learn available actions and load the city map |
| 1 | Map Analysis | Parse 11×11 grid, classify roads and tall buildings |
| 2 | Mission Planning | BFS pathfinding for transporter routes, cluster targets, budget estimates |
| 3 | Deployment & Search | Create units, move transporters, deploy scouts, inspect tall buildings |
| 4 | Evacuation | `callHelicopter` to confirmed survivor location |

## Configuration

**`.env`** (in `Domatowo/` — not committed):
```env
Hub__ApiKey=<your-api-key>
Hub__ApiUrl=https://<hub_url>
```

**`appsettings.json`** defaults:
```json
{
  "Agent":  { "Provider": "lmstudio", "Model": "qwen3-coder-30b-a3b-instruct-mlx", "Endpoint": "http://localhost:1234/v1" },
  "Vision": { "Provider": "lmstudio", "Model": "qwen/qwen3-vl-8b", "Endpoint": "http://localhost:1234/v1" },
  "Hub":    { "TaskName": "domatowo", "MaxRetries": 3, "RetryDelayMs": 500 },
  "Telemetry": { "Enabled": true, "ServiceName": "Domatowo" }
}
```

The LLM runs locally via [LM Studio](https://lmstudio.ai/) at `http://localhost:1234/v1`.

## Running

```bash
# Direct run (agent only)
dotnet run --project Domatowo/Domatowo.csproj

# With Aspire observability dashboard
dotnet run --project Domatowo.AppHost/Domatowo.AppHost.csproj

# Build only
dotnet build Domatowo/Domatowo.csproj

# View logs
cat Domatowo/logs/*.log
```

## API Protocol

All calls POST to `{Hub__ApiUrl}/verify`:

```json
{
  "apikey": "<Hub__ApiKey>",
  "task": "domatowo",
  "answer": { "action": "..." }
}
```

Key actions: `help`, `getMap`, `create`, `move`, `deploy`, `inspect`, `getLogs`, `callHelicopter`.
