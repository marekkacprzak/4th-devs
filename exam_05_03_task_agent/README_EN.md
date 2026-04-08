# ShellAccess — Remote Shell Intelligence Agent

A C# agent that executes shell commands on a remote server via the hub API to search log files, locate when and where "Rafał" was found, and submit the answer as a JSON payload.

## Overview

The agent implements a single-phase agentic pipeline:

1. **Agentic Shell Exploration**: An `AgentOrchestrator` loop (Microsoft Agent Framework) drives the LLM to iteratively execute shell commands on the remote server via `ExecuteShellCommand`. The agent:
   - Explores `/data` with `ls`, `find`, and `grep`
   - Searches for log entries mentioning "Rafał"
   - Extracts the finding date, city name, and geographic coordinates
   - Computes the date one day before the finding
   - Submits the final answer by executing an `echo` command that outputs the JSON

## Architecture

```
ShellAccess/                   — Main agent project (ASP.NET Web, net10.0)
  Program.cs                   — Entry point: .env loading, config, agent setup and run
  Config/                      — AgentConfig, HubConfig, TelemetryConfig
  Adapters/OpenAiClientFactory — Creates IChatClient for lmstudio/openai providers
  Services/
    AgentOrchestrator          — Microsoft.Extensions.AI agentic loop (max 20 iterations)
    CentralaApiClient          — HTTP POST to /verify with retry, rate limiting, tracing
    RunLogger                  — Timestamped file logger (logs/ directory)
  Tools/ShellTools             — ExecuteShellCommand tool: wraps hub API shell execution
  Telemetry/TelemetrySetup     — OpenTelemetry TracerProvider + MeterProvider (OTLP)
  UI/ConsoleUI                 — Spectre.Console rich terminal output

ShellAccess.AppHost/           — .NET Aspire host for observability dashboard
```

## Prerequisites

- **.NET 10 SDK**
- **LM Studio** running at `http://localhost:1234/v1` with:
  - `qwen3-coder-30b-a3b-instruct-mlx` — agent model

## Configuration

Create a `.env` file in the `ShellAccess/` directory:

```
Hub__VerifyUrl=https://<hub_url>/verify
Hub__ApiKey=your-api-key-here
```

All other settings have defaults in `appsettings.json`:

```json
{
  "Agent": { "Provider": "lmstudio", "Model": "qwen3-coder-30b-a3b-instruct-mlx" },
  "Hub":   { "VerifyUrl": "", "ApiKey": "", "TaskName": "shellaccess" }
}
```

## Running

```bash
# Restore packages
dotnet restore

# Run the agent directly
dotnet run --project ShellAccess

# Run with Aspire observability dashboard (telemetry)
dotnet run --project ShellAccess.AppHost
```

## Output

- **Console** — Rich terminal output via Spectre.Console showing each LLM call, shell command, and API response
- **Log file** — Full structured log at `ShellAccess/logs/YYYY-MM-DD_HH-mm-ss.log` with all requests and responses untruncated
- **Telemetry** — OpenTelemetry traces and metrics exported via OTLP (visible in Aspire dashboard when using AppHost)

## API Protocol

```
POST https://<hub_url>/verify

Shell command: { "apikey": "...", "task": "shellaccess", "answer": { "cmd": "ls -la /data" } }

Final answer:  echo '{"date":"YYYY-MM-DD","city":"city name","longitude":XX.XXXXXX,"latitude":YY.YYYYYY}'
```

The agent finds: **date** (one day before Rafał was found), **city** (where he was found), **longitude** and **latitude** (coordinates of the location).
