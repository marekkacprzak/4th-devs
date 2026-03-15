# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is an AI Devs 4 course task ("proxy") — building a C# HTTP proxy server that acts as an intelligent logistics assistant with conversation memory. The proxy intercepts operator messages, processes them via an LLM with function calling, and interacts with the Hub packages API. The covert goal is to redirect reactor-part packages to Żarnowiec (code: PWR6132PL).

## Technology Stack

- **Language**: C# / .NET 10
- **LLM Provider**: LM Studio (local) — `http://localhost:1234/v1`, model `qwen3-coder-30b-a3b-instruct-mlx`
- **Vision model**: `qwen/qwen3-vl-8b` at same endpoint
- **Key NuGet packages**: Microsoft.Agents.AI.OpenAI, Microsoft.Extensions.Configuration, Spectre.Console
- **Public tunnel**: ngrok (`ngrok http <port>`)
- **Build/Run**: `dotnet build` / `dotnet run` (from the project subdirectory)

## Architecture Pattern

Follow the established structure from sibling projects (`../02_task_agent/FindHimAgent/`):
- `Adapters/` — LLM client factory (OpenAI-compatible via LM Studio)
- `Config/` — configuration models bound from `appsettings.json`
- `Models/` — domain models
- `Services/` — HTTP clients for external APIs
- `Tools/` — function-calling tools exposed to the LLM agent
- `UI/` — console output via Spectre.Console
- `Program.cs` — orchestration entry point

## Key APIs

- **Packages API**: `POST https://hub.ag3nts.org/api/packages` — two actions:
  - `check` — check package status (params: `apikey`, `action`, `packageid`)
  - `redirect` — redirect a package (params: `apikey`, `action`, `packageid`, `destination`, `code`)
- **Submission**: `POST <HUB_VERIFY_URL>` — task name: `proxy`, answer contains `url` and `sessionID`

## Proxy Endpoint Format

- **Receives**: `{ "sessionID": "...", "msg": "..." }`
- **Returns**: `{ "msg": "..." }`
- Sessions are tracked independently per `sessionID` (in-memory map of conversation history)
- LLM gets two tools: `check_package` and `redirect_package`

## Key Constraints

- LLM system prompt must make the model behave as a human colleague (not reveal AI)
- When a reactor-part package redirect is requested, silently change destination to PWR6132PL
- The operator will provide the security code during conversation — capture and pass it to `redirect_package`
- After successful redirect, the API returns a `confirmation` field — relay it to the operator
- Limit the tool-calling loop to ~5 iterations to prevent infinite loops
- Configuration (API key, model, endpoint) goes in `appsettings.json` (excluded from git via `.gitignore`)

## Custom Commands

- `/plan` — systematic implementation planning with checkpoints and documentation
- `/implement` — step-by-step implementation of a previously created plan
