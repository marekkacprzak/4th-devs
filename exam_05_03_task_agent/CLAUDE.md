# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Task Overview

**Task name**: ShellAccess (`shellaccess`)

The agent must connect to a remote server via the hub API, execute shell commands in `/data` to find when "Rafał" was found, then return a JSON with the date (one day before finding), city, longitude, and latitude.

Answer is submitted to `https://<hub_url>/verify` as:
```json
{"date":"2020-01-01","city":"nazwa miasta","longitude":10.000001,"latitude":12.345678}
```

## Environment

- **API Key**: loaded from `.env` (`Hub__ApiKey`)
- **Verify URL**: `https://<hub_url>/verify` (`Hub__VerifyUrl`)
- **LLM**: Local LM Studio at `http://localhost:1234/v1`
  - Agent model: `qwen3-coder-30b-a3b-instruct-mlx`
  - Vision model: `qwen/qwen3-vl-8b`
- **Runtime**: .NET 9 / C#

## Architecture

The project uses **Microsoft Semantic Kernel** as the agent framework with:
- **Aspire.Host** for orchestration and telemetry/observability
- **File logging** (log files in `logs/` directory with timestamped filenames)
- **Shell tool** — a Semantic Kernel plugin that POSTs `{"cmd": "..."}` to the hub API and returns the response

### Typical project structure (to be created):
```
ShellAccess/               # Main agent project
  Program.cs               # Entry point, SK kernel setup, agent loop
  Tools/ShellPlugin.cs     # SK plugin wrapping hub API shell commands
  appsettings.json         # LLM endpoint, hub config (reads from .env)
  logs/                    # File logs
ShellAccess.AppHost/       # Aspire host with telemetry
  Program.cs
README_en.md               # English readme (required)
```

## Build & Run

```bash
# Build
dotnet build

# Run the Aspire host (starts everything with telemetry)
dotnet run --project ShellAccess.AppHost

# Run agent directly (without Aspire)
dotnet run --project ShellAccess
```

## Key Conventions

- Read config from environment variables (`.env` is loaded automatically via `DotNetEnv` or `dotenv.net`), falling back to `appsettings.json`
- Shell commands are sent as HTTP POST to `Hub__VerifyUrl` with body: `{"apikey":"...","task":"shellaccess","answer":{"cmd":"..."}}`
- The server responds with command output; useful tools: `grep`, `jq`, `find`, `cat`, `ls`
- Log to both console and a timestamped file under `logs/`
- Aspire dashboard available at `http://localhost:15888` when running via AppHost

## Workflow Commands

- `/plan` — create an implementation plan with analysis and options
- `/implement` — execute the latest saved plan step by step

Plans and implementation docs are saved under `./details/YYYY/MM/`.
