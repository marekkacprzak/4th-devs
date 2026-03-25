# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

AI Devs 4 course task ("reactor") — building a C# agent using Microsoft Agent Framework that guides a robot through a 7×5 grid reactor, avoiding moving reactor blocks. The robot starts at column 1, row 5 and must reach column 7, row 5.

## Task Mechanics

- **Board**: 7 columns × 5 rows. Robot always on row 5 (bottom).
- **Robot commands**: `start`, `reset`, `left`, `wait`, `right` — one per API call.
- **Blocks move only when a command is sent** (sending `wait` advances block state without moving the robot).
- **Each block** occupies exactly 2 cells and moves cyclically up/down (reverses at top/bottom extremes).
- **Algorithm**:
  1. Always send `start` first.
  2. Check board state; decide if moving right is safe.
  3. If unsafe ahead, wait.
  4. If current column is also threatened, escape left.
  5. Repeat until column 7 is reached.

### API Format

```json
{
  "apikey": "<key>",
  "task": "reactor",
  "answer": {
    "command": "start"
  }
}
```

API endpoint: POST to Hub `/verify`.

Board symbols: `P` = start, `G` = goal, `B` = reactor block, `.` = empty.

## Technology Stack

- **Language**: C# / .NET
- **AI Framework**: Microsoft Agent Framework (use `context7` MCP tool for docs)
- **Observability**: .NET Aspire AppHost with OTLP telemetry
- **LLM (Agent)**: LM Studio local — `http://localhost:1234/v1`, model `qwen3-coder-30b-a3b-instruct-mlx`
- **Vision**: `qwen/qwen3-vl-8b` at same endpoint
- **Embedding**: `text-embedding-nomic-embed-text-v1.5` at same endpoint
- **Build/Run**: `dotnet build` / `dotnet run` from the agent project directory

## Expected Project Structure

Mirror the pattern from `../exam_02_02_task_agent/ElectricityAgent/`:

```
ReactorAgent/               ← main agent project
  Adapters/                 ← LLM client factory (OpenAI-compatible via LM Studio)
  Config/                   ← config models bound from appsettings.json
  Models/                   ← board state, block positions, direction enum
  Services/                 ← HubApiClient (send command, parse board response)
  Tools/                    ← function-calling tools: SendCommand, GetBoardState
  Telemetry/                ← TelemetrySetup (OTLP via OpenTelemetry)
  UI/                       ← ConsoleUI (Spectre.Console)
  Program.cs                ← agent orchestration entry point
  appsettings.json
ReactorAgent.AppHost/       ← Aspire host project
  AppHost.cs
  ReactorAgent.AppHost.csproj
reactor.sln
```

## Configuration (`appsettings.json`)

```json
{
  "Agent": { "Provider": "lmstudio", "Model": "qwen3-coder-30b-a3b-instruct-mlx", "Endpoint": "http://localhost:1234/v1" },
  "Hub": { "ApiUrl": "", "ApiKey": "", "TaskName": "reactor" },
  "Telemetry": { "Enabled": true, "OtlpEndpoint": "http://localhost:4317", "ServiceName": "ReactorAgent", "EnableSensitiveData": true }
}
```

Secrets go in `.env` (loaded manually in `Program.cs` before `ConfigurationBuilder`).

## Key Design Considerations

- **Deterministic navigation first**: Implement the stated algorithm (move right → wait → escape left) as a deterministic state machine before involving the LLM, to keep cost/latency low.
- **LLM as decision-maker**: The agent can use the LLM to interpret the board state string and return the next command — this is the "Agent Framework" usage pattern.
- **Board parsing**: The API response contains the board as a text grid. Parse block positions and movement direction from the response JSON.
- **Safety lookahead**: Before moving right, check if the next column is free at row 5 *and* that no block is one step away from descending into it on the next turn.
- **Max iteration guard**: Set a step limit (e.g., 50) to prevent infinite loops.
- **Aspire AppHost**: Reference the agent project from AppHost using `builder.AddProject<Projects.ReactorAgent>("reactor-agent")`. The `.aspire/settings.json` already points to `../FirmwareAgent.AppHost/FirmwareAgent.AppHost.csproj` — update the project name accordingly.

## Custom Commands

- `/plan` — systematic implementation planning with checkpoints and documentation
- `/implement` — step-by-step implementation of a previously created plan
