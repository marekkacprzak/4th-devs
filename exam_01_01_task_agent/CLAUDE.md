# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is an AI Devs 4 course task ("people") — building a C# agent using Microsoft Agent Framework that:
1. Downloads a `people.csv` file from `<HUB_DATA_URL>/<API_KEY>/people.csv`
2. Filters people by criteria: male, born in Grudziądz, age 20-40 in 2026 (born 1986-2006)
3. Tags each person's job using an LLM with Structured Output (available tags: IT, transport, edukacja, medycyna, praca z ludźmi, praca z pojazdami, praca fizyczna)
4. Selects only people tagged with "transport"
5. Submits results to `<HUB_VERIFY_URL>` (task name: `people`)

## Technology Stack

- **Language**: C# / .NET
- **AI Framework**: Microsoft Agent Framework (use context7 MCP tool to look up docs)
- **LLM Provider**: LM Studio (local) — `http://localhost:1234/v1`, model `qwen3-coder-30b-a3b-instruct-mlx`
- **Vision model**: `qwen/qwen3-vl-8b` at same endpoint
- **Build/Run**: `dotnet build` / `dotnet run`
- **Reference project**: `../exam_01_04_task_agent/SpkAgent/` — same architecture pattern (Adapters, Config, Services, Tools, UI directories)

## Submission Format

POST to `<HUB_VERIFY_URL>`:
```json
{
  "apikey": "<key>",
  "task": "people",
  "answer": [
    {
      "name": "Jan",
      "surname": "Kowalski",
      "gender": "M",
      "born": 1987,
      "city": "Warszawa",
      "tags": ["tag1", "tag2"]
    }
  ]
}
```

## Key Constraints

- Use Structured Output (JSON Schema in `response_format`) for LLM tag classification
- Batch multiple job descriptions in a single LLM call to reduce API calls
- `born` field is an integer (year only), `tags` is an array of strings
- One person can have multiple tags; only include people with "transport" tag in final answer

## Custom Commands

- `/plan` — systematic implementation planning with checkpoints and documentation
- `/implement` — step-by-step implementation of a previously created plan
