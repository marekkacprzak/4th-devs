# WindPower — Wind Turbine Scheduler Agent

AI Devs 4, Exam Task 5 — `windpower`

## Overview

This agent configures the schedule of a wind turbine within a **40-second service window** (battery-powered control system). It analyzes a weather forecast from the Centrala API, determines which hours are dangerous (storms), schedules turbine protection (pitch angle 90°, idle mode), and finds the first optimal window for power production.

## Architecture

```
WindPower/                      .NET 10 agent project
  Program.cs                    Entry point — wires services, runs orchestrator
  Adapters/OpenAiClientFactory  Creates IChatClient from OpenAI SDK / LM Studio
  Config/AgentConfig.cs         AgentConfig + HubConfig POCOs
  Config/TelemetryConfig.cs     Telemetry POCO
  Models/VerifyRequest.cs       DTO for /verify requests
  Models/WindpowerModels.cs     WeatherEntry, TurbineSpecsData, ConfigEntry
  Services/WindpowerOrchestrator  Core 4-phase C#-driven orchestrator
  Services/CentralaApiClient    HTTP client with retry + rate-limit logic
  Services/RunLogger            File-based structured run logger
  Telemetry/TelemetrySetup      OpenTelemetry TracerProvider + MeterProvider
  Tools/WindpowerTools.cs       CallVerifyApi tool (LLM-callable wrapper)
  UI/ConsoleUI.cs               Spectre.Console rich output helpers
  logs/                         Auto-generated run logs (gitignored)

WindPower.AppHost/              .NET Aspire host for observability dashboard
```

## How It Works

The `WindpowerOrchestrator` runs in **4 phases** — all C#-driven (no LLM in the critical path):

| Phase | Action | Deadline |
|-------|--------|----------|
| 0 — Discovery | Call `help` to learn API actions (before timer starts) | N/A |
| 1 — Data Collection | Call `start` (starts 40s timer), fire `weatherForecast` + `turbineSpecs` + `powerRequirements` in parallel, poll `getResult` until all 3 collected | T+20s |
| 2 — Analysis | Parse weather entries, detect storm blocks, build config schedule | T+22s |
| 3 — Configuration | Fire `unlockCodeGenerator` for every config entry (parallel), poll `getResult` for codes, send batch `config`, run `turbinecheck`, send `done` | T<40s |

### Storm Protection Logic

- Storm = hour where `windSpeed > maxWindSpeed` (from turbine specs)
- When a new storm starts with the turbine in normal mode → add `pitchAngle=90, turbineMode=idle` config
- Turbine auto-resets to normal ~1 hour after the last storm hour ends
- If storms are separated by >1 hour gap, the turbine resets and needs another protection config

### Production Window

First hourly slot where:
- Wind speed is between `cutInSpeed` and `maxWindSpeed`
- Turbine is not in storm or recovery mode

Sets `pitchAngle=optimalPitchAngle, turbineMode=production`.

## Configuration

Copy the `.env` file or set these environment variables:

```env
Hub__ApiKey=<your-api-key>
Hub__ApiUrl=https://<hub_url>
```

`appsettings.json` contains non-secret defaults:

```json
{
  "Agent": {
    "Provider": "lmstudio",
    "Model": "qwen3-coder-30b-a3b-instruct-mlx",
    "Endpoint": "http://localhost:1234/v1"
  },
  "Hub": {
    "TaskName": "windpower",
    "MaxRetries": 3,
    "RetryDelayMs": 500
  }
}
```

## Running

```bash
# Run the agent directly
dotnet run --project WindPower/WindPower.csproj

# Run with Aspire observability dashboard
dotnet run --project WindPower.AppHost/WindPower.AppHost.csproj

# Check run logs
cat WindPower/logs/*.log
```

## Success Criteria

Agent receives a flag string from Centrala in the `done` response within 40 seconds of calling `start`.
