# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is an AI Devs 4 course task ("electricity") — building a C# agent using Microsoft Agent Framework that solves an electrical cable puzzle on a 3x3 grid. The goal is to rotate grid tiles (each containing cable connectors) so that power flows from the emergency power source (bottom-left) to three power plants: PWR6132PL, PWR1593PL, PWR7264PL.

## Task Mechanics

- **Board**: 3x3 grid, fields addressed as `AxB` (A=row 1-3 top-down, B=column 1-3 left-right)
- **Only operation**: Rotate a field 90° clockwise via POST to `<HUB_VERIFY_URL>`
- **One rotation per API call**; to rotate left, send 3 clockwise rotations
- **Board state**: Fetched as PNG from `<HUB_DATA_URL>/<API_KEY>/electricity.png`
- **Reset**: GET `<HUB_DATA_URL>/<API_KEY>/electricity.png?reset=1`
- **Success**: Hub returns `{FLG:...}` when configuration is correct

### API Format

```json
{
  "apikey": "<key>",
  "task": "electricity",
  "answer": {
    "rotate": "2x3"
  }
}
```

## Technology Stack

- **Language**: C# / .NET
- **AI Framework**: Microsoft Agent Framework (use context7 MCP tool to look up docs)
- **LLM Provider**: LM Studio (local) — `http://localhost:1234/v1`, model `qwen3-coder-30b-a3b-instruct-mlx`
- **Vision model**: `qwen/qwen3-vl-8b` at same endpoint (for interpreting board PNG)
- **Embedding model**: `text-embedding-nomic-embed-text-v1.5` at same endpoint
- **Build/Run**: `dotnet build` / `dotnet run` (from the project subdirectory)

## Architecture Pattern

Follow the same structure as `../exam_01_02_task_agent/FindHimAgent/`:
- `Adapters/` — LLM client factory (OpenAI-compatible via LM Studio)
- `Config/` — configuration models bound from `appsettings.json`
- `Models/` — domain models (grid tile, board state, cable directions)
- `Services/` — HTTP clients for Hub API (rotate, fetch board image, verify)
- `Tools/` — function-calling tools exposed to the LLM agent (rotate tile, get board state, reset board)
- `UI/` — console output via Spectre.Console
- `Program.cs` — orchestration entry point

## Key Design Considerations

- **Vision subagent**: The board state is a PNG image. Use a vision model to interpret each tile's cable connections (which edges have cables: top/bottom/left/right). Delegate image interpretation to a separate tool or subagent rather than doing it in the main agent loop.
- **Image preprocessing**: Consider cropping individual tiles from the 3x3 grid before sending to the vision model for better accuracy.
- **Function Calling**: The LLM agent should autonomously read the board, compute required rotations, and send them sequentially.
- **Verification loop**: After sending rotations, re-fetch the board image to verify correctness before declaring success.
- **Max iteration limit**: Set 10-15 to prevent infinite agent loops.

## Custom Commands

- `/plan` — systematic implementation planning with checkpoints and documentation
- `/implement` — step-by-step implementation of a previously created plan
