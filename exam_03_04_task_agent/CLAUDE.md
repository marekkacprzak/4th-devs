# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is an AI Devs 4 course task ("negotiations") — building a C# HTTP server that exposes 1–2 tool endpoints for an autonomous AI agent to find survivor cities offering all required wind turbine components. The agent POSTs natural-language queries to your endpoints and expects structured JSON responses. Task name for verification: `negotiations`.

## Technology Stack

- **Language**: C# / .NET 10
- **LLM Provider**: LM Studio (local) — `http://localhost:1234/v1`
  - Agent model: `qwen3-coder-30b-a3b-instruct-mlx`
  - Vision model: `qwen/qwen3-vl-8b`
  - Embedding model: `text-embedding-nomic-embed-text-v1.5`
- **Key NuGet packages**: Microsoft.Agents.AI.OpenAI, Microsoft.Extensions.Configuration, Spectre.Console, OpenTelemetry
- **Observability**: Aspire.Hosting with built-in dashboard + OpenTelemetry OTLP export
- **Public tunnel**: ngrok (`ngrok http <port>`)
- **Build/Run**: `dotnet build` / `dotnet run` (from project subdirectory)
- **With Aspire dashboard**: `dotnet run --project ReactorAgent.AppHost`
- **Hot reload**: `dotnet watch run --project ReactorAgent`

## Architecture Pattern

Follow the structure from the sibling reference project (`../exam_01_03_task_agent/ProxyAgent/`):

- `Adapters/` — LLM client factory (OpenAI-compatible via LM Studio)
- `Config/` — config models bound from `appsettings.json` (AgentConfig, HubConfig, ProxyConfig, TelemetryConfig)
- `Models/` — HTTP request/response models
- `Services/` — AgentOrchestrator (tool-calling loop, max 5 iterations), SessionManager, HubApiClient
- `Tools/` — function-calling tools (max 2) exposed to the LLM
- `Telemetry/` — OpenTelemetry setup
- `UI/` — console output via Spectre.Console
- `Program.cs` — DI, Minimal API endpoints, orchestration entry point
- `ReactorAgent.AppHost/` — Aspire host project

## Tool Endpoint Contract

The external agent interacts with your endpoints like this:

- **Request** (POST): `{ "params": "natural language description of what the agent needs" }`
- **Response**: `{ "output": "answer for the agent" }`
- Response must be **4–500 bytes**
- Agent has **max 10 steps** total; it searches for **3 items**
- Register tool URLs at `POST https://<hub_api>/verify` with task `negotiations`

## Data Source

CSV knowledge base at `https://<hub_api>/dane/s03e04_csv/` — download and parse at startup or embed as static data. Tools must handle natural-language item names (e.g. "potrzebuję kabla długości 10 metrów" instead of "kabel 10m").

## Verification Flow

Submit tools:
```json
{
  "apikey": "<key>",
  "task": "negotiations",
  "answer": {
    "tools": [
      { "URL": "https://your-domain/api/tool1", "description": "What it does and what params to pass" }
    ]
  }
}
```

Check result (send after 30–60 seconds):
```json
{
  "apikey": "<key>",
  "task": "negotiations",
  "answer": { "action": "check" }
}
```

Debug panel: `https://<hub_api>/debug`

## Key Constraints

- Maximum **2 tools** registered (1 is sufficient)
- Tool response size: **4–500 bytes** — keep answers concise
- Agent sends params in **natural language**, not structured form — use fuzzy/semantic matching
- Tool descriptions must clearly state what parameters to pass and what the tool returns
- `appsettings.json` holds API key and model config — exclude from git

## Custom Commands

- `/plan` — systematic implementation planning with checkpoints and documentation
- `/implement` — step-by-step implementation of a previously created plan
