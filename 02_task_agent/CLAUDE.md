# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is an AI Devs 4 course task ("findhim") — building a C# agent using Microsoft Agent Framework that:
1. Takes the list of suspects from the previous task (01_task_agent / "people" task — people tagged as "transport")
2. For each suspect, queries `POST https://hub.ag3nts.org/api/location` to get their GPS coordinates
3. Downloads power plant locations from `<HUB_DATA_URL>/<API_KEY>/findhim_locations.json`
4. Computes geographic distance (Haversine formula) to find which suspect was closest to a power plant
5. Queries `POST https://hub.ag3nts.org/api/accesslevel` with the suspect's name, surname, and birthYear (integer)
6. Submits the answer to `POST <HUB_VERIFY_URL>` (task name: `findhim`)

## Technology Stack

- **Language**: C# / .NET
- **AI Framework**: Microsoft Agent Framework (use context7 MCP tool to look up docs)
- **LLM Provider**: LM Studio (local) — `http://localhost:1234/v1`, model `qwen3-coder-30b-a3b-instruct-mlx`
- **Vision model**: `qwen/qwen3-vl-8b` at same endpoint
- **Build/Run**: `dotnet build` / `dotnet run` (from the project subdirectory)

## Architecture Pattern

Follow the same structure as `../01_task_agent/PeopleAgent/`:
- `Adapters/` — LLM client factory (OpenAI-compatible via LM Studio)
- `Config/` — configuration models bound from `appsettings.json`
- `Models/` — domain models (suspect, power plant, location, etc.)
- `Services/` — HTTP clients for Hub API endpoints (location, accesslevel, verify)
- `Tools/` — function-calling tools exposed to the LLM agent
- `UI/` — console output via Spectre.Console
- `Program.cs` — orchestration entry point

## Submission Format

```json
{
  "apikey": "<key>",
  "task": "findhim",
  "answer": {
    "name": "Jan",
    "surname": "Kowalski",
    "accessLevel": 3,
    "powerPlant": "PWR1234PL"
  }
}
```

## Key Constraints

- Use Function Calling so the LLM agent can iterate through suspects and call tools autonomously
- `birthYear` must be an integer (extract year if you have a full date)
- Power plant codes follow format `PWR0000PL`
- Set a max iteration limit (10-15) to prevent infinite agent loops
- The suspect list comes from the 01_task_agent results (people from CSV who matched the "transport" criteria)

## Custom Commands

- `/plan` — systematic implementation planning with checkpoints and documentation
- `/implement` — step-by-step implementation of a previously created plan
