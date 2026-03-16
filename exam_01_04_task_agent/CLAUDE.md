# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is an AI Devs 4 course task ("sendit") — building a C# agent using Microsoft Agent Framework that:
1. Downloads shipping documentation from `https://hub.REDACTED.org/dane/doc/` (starting with `index.md`, then all referenced files)
2. Parses the SPK (System Przesyłek Konduktorskich) regulations, route network, fee tables, and declaration template
3. Fills out a transport declaration form with specific shipment data
4. Submits the completed declaration to `<HUB_VERIFY_URL>` for validation

## Technology Stack

- **Language**: C# / .NET
- **AI Framework**: Microsoft Agent Framework (use context7 MCP tool to look up docs)
- **LLM Provider**: LM Studio (local) — `http://localhost:1234/v1`, model `qwen3-coder-30b-a3b-instruct-mlx`
- **Build/Run**: `dotnet build` / `dotnet run`

## Shipment Data for Declaration

- Sender ID: `450202122`
- Origin: Gdańsk
- Destination: Żarnowiec
- Weight: 2800 kg (2.8 tons)
- Contents: reactor fuel cassettes (kasety z paliwem do reaktora)
- Budget: 0 PP (must be free / funded by System)
- Special notes: NONE (leave empty — manually verified shipments get flagged)

## Submission Format

POST to `<HUB_VERIFY_URL>`:
```json
{
  "apikey": "<key>",
  "task": "sendit",
  "answer": {
    "declaration": "<full declaration text matching template format exactly>"
  }
}
```

## Key Constraints

- Declaration format must match the template from documentation exactly (separators, field order, formatting)
- Route code must be looked up from the route network documentation
- Fee category must allow 0 PP cost (find which categories are System-funded)
- Some documentation files may be images requiring vision processing
- CSV files contain essential data for filling the declaration

## Custom Commands

- `/plan` — systematic implementation planning with checkpoints and documentation
- `/implement` — step-by-step implementation of a previously created plan
