# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is an AI Devs 4 course task ("drone") — building a C# agent using Microsoft Agent Framework that:
1. Analyzes a grid map image to locate a dam sector (use vision model)
2. Reads drone API documentation from `https://hub.REDACTED.org/dane/drone.html`
3. Sends drone flight instructions to `Hub__ApiUrl`
4. Iterates based on API error feedback until receiving a flag `{FLG:...}`

Target: Dam near Żarnowiec power plant. Power plant code: `PWR6132PL`. Task name: `drone`.

## Technology Stack

- **Language**: C# / .NET
- **AI Framework**: Microsoft Agent Framework (use context7 MCP tool to look up docs)
- **Observability**: .NET Aspire Host with telemetry
- **LLM Provider**: LM Studio (local) — `http://localhost:1234/v1`
  - Agent model: `qwen3-coder-30b-a3b-instruct-mlx`
  - Vision model: `qwen/qwen3-vl-8b`
  - Embedding model: `text-embedding-nomic-embed-text-v1.5`

## Build & Run

```bash
dotnet build DroneAgent/DroneAgent.csproj
dotnet run --project DroneAgent/
```

Aspire host with telemetry dashboard:
```bash
dotnet run --project DroneAgent.AppHost/
```

## Project Structure Convention

Follow the pattern from `exam_02_03_task_agent/FailureAgent`:
- `<ProjectName>/` — main agent project
- `<ProjectName>.AppHost/` — Aspire host with telemetry
- Subdirectories: `Tools/`, `Config/`, `Services/`, `Adapters/`, `Telemetry/`, `UI/`

## Submission Format

POST to `Hub__ApiUrl`:
```json
{
  "apikey": "<key>",
  "task": "drone",
  "answer": {
    "instructions": ["instrukcja1", "instrukcja2", "..."]
  }
}
```

## Key Strategy

- **Two-phase approach**: First use the vision model to analyze the map image and identify the dam sector (column, row — 1-indexed grid), then use the text agent model to handle drone API interaction.
- **Drone API has intentional traps**: The documentation contains conflicting function names with different behaviors depending on parameters. Only configure what's strictly needed for the mission.
- **Reactive iteration**: Send best-guess instructions, read error messages, and adjust. API errors are precise and helpful.
- **hardReset**: Use if accumulated bad config causes cascading errors.

## Custom Commands

- `/plan` — systematic implementation planning with checkpoints and documentation
- `/implement` — step-by-step implementation of a previously created plan
