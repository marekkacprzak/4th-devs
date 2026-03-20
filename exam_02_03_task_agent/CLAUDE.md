# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is an AI Devs 4 course task ("failure") — building a C# agent using Microsoft Agent Framework that:
1. Downloads a large log file from `Hub__DataBaseUrl/<apikey>/failure.log`
2. Filters and compresses logs to only power-plant-relevant events (power, cooling, water pumps, software, subsystems)
3. Condenses the output to fit within 1500 tokens while preserving: timestamps, severity levels, and component IDs
4. Submits condensed logs to `Hub__ApiUrl` and iterates based on technician feedback

## Technology Stack

- **Language**: C# / .NET
- **AI Framework**: Microsoft Agent Framework (use context7 MCP tool to look up docs)
- **Observability**: .NET Aspire Host with telemetry
- **LLM Provider**: LM Studio (local) — `http://localhost:1234/v1`
  - Agent model: `qwen3-coder-30b-a3b-instruct-mlx`
  - Vision model: `qwen/qwen3-vl-8b`
  - Embedding model: `text-embedding-nomic-embed-text-v1.5`
- **Build/Run**: `dotnet build` / `dotnet run`

## Project Structure Convention

Follow the pattern from `exam_01_04_task_agent/SpkAgent`:
- `<ProjectName>/` — main agent project
- `<ProjectName>.AppHost/` — Aspire host with telemetry
- Subdirectories: `Tools/`, `Config/`, `Services/`, `Adapters/`, `Telemetry/`, `UI/`

## Submission Format

POST to `Hub__ApiUrl`:
```json
{
  "apikey": "<key>",
  "task": "failure",
  "answer": {
    "logs": "<condensed log lines separated by \\n>"
  }
}
```

## Log Format Requirements

- One event per line
- Date format: `YYYY-MM-DD`
- Time format: `HH:MM` or `H:MM`
- Each line must preserve: timestamp, severity level, component ID
- Descriptions can be shortened/paraphrased
- Total must not exceed 1500 tokens

## Key Constraints

- The raw log file is very large — use a subagent or tool-based approach to search/filter rather than loading everything into the main agent's context
- Feedback from Centrala is precise — it specifies which subsystems couldn't be analyzed, use it to iteratively improve the log selection
- Count tokens before submitting (conservative estimate) to avoid rejection
- Consider starting with a smaller set and expanding based on feedback rather than trying to include everything at once

## Custom Commands

- `/plan` — systematic implementation planning with checkpoints and documentation
- `/implement` — step-by-step implementation of a previously created plan
