# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is an AI Devs 4 course task ("mailbox") — building a C# agent using Microsoft Agent Framework that:
1. Connects to the `zmail` API at `POST https://<hubapi>/api/zmail` to search an email inbox
2. Finds emails from Wiktor (sent from a `proton.me` domain) who informed on the team to System operators
3. Extracts three pieces of information: attack date (`YYYY-MM-DD`), employee system password, and a security confirmation code (`SEC-` + 28 chars)
4. Submits answers to `Hub__ApiUrl` with task name `mailbox`

The inbox is **live** — new emails may arrive during execution. The agent must handle retries and re-searches.

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

Follow the pattern from `exam_02_03_task_agent/FailureAgent`:
- `<ProjectName>/` — main agent project
- `<ProjectName>.AppHost/` — Aspire host with telemetry
- Subdirectories: `Tools/`, `Config/`, `Services/`, `Adapters/`, `Telemetry/`, `UI/`

## Zmail API

- `action: "help"` — discover available actions and parameters
- `action: "getInbox"` — list emails (paginated, metadata only)
- Search supports Gmail-like operators: `from:`, `to:`, `subject:`, `OR`, `AND`
- Two-step data retrieval: search first (metadata), then fetch full message content by ID

## Submission Format

POST to `Hub__ApiUrl`:
```json
{
  "apikey": "<key>",
  "task": "mailbox",
  "answer": {
    "password": "<found-password>",
    "date": "YYYY-MM-DD",
    "confirmation_code": "SEC-<28-chars>"
  }
}
```

## Key Constraints

- Agent loop: search → read → extract → search more → submit → iterate based on hub feedback
- The inbox is active — if something isn't found, retry later as new messages may have arrived
- Use hub feedback to identify which values are still missing or incorrect
- API key is stored in config (appsettings.json), never hardcoded

## Custom Commands

- `/plan` — systematic implementation planning with checkpoints and documentation
- `/implement` — step-by-step implementation of a previously created plan
