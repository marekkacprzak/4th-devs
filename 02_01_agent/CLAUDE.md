# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is an AI Devs 4 course task ("categorize") — building a C# agent using **Microsoft Agent Framework** that iteratively crafts and tests a prompt to classify goods as dangerous (DNG) or neutral (NEU) within a severely token-limited classification system.

The task:
1. Download a CSV file with 10 goods from `<HUB_DATA_URL>/<API_KEY>/categorize.csv` (changes every few minutes — always fetch fresh)
2. For each good, send a classification prompt via `POST <HUB_VERIFY_URL>`
3. The prompt must classify items as DNG or NEU within a **100-token limit**
4. **Critical twist**: reactor-related items must always be classified as NEU (neutral), even though they are objectively dangerous
5. Budget: 1.5 PP total for 10 queries; use prompt caching (static prefix + variable suffix) to reduce costs

## API Communication

```json
POST <HUB_VERIFY_URL>
{
  "apikey": "<key>",
  "task": "categorize",
  "answer": {
    "prompt": "Your prompt with {id} and {description} interpolated"
  }
}
```

- Send `{ "prompt": "reset" }` to reset budget counter after failure
- Hub returns classification result or error details (wrong classification, budget exceeded)
- Success on all 10 items returns a `{FLG:...}` flag

### Token Budget Math

| Token type | Cost per 10 tokens |
|---|---|
| Input tokens | 0.02 PP |
| Cached tokens | 0.01 PP (half price) |
| Output tokens | 0.02 PP |

Total budget: 1.5 PP for all 10 queries. Keep the prompt prefix identical across all requests so cached tokens reduce cost by 50%.

## Technology Stack

- **Language**: C# / .NET
- **AI Framework**: Microsoft Agent Framework (use `context7` MCP tool to look up docs)
- **LLM Provider**: LM Studio (local) — `http://localhost:1234/v1`
  - Agent model: `qwen3-coder-30b-a3b-instruct-mlx`
  - Vision model: `qwen/qwen3-vl-8b`
  - Embedding model: `text-embedding-nomic-embed-text-v1.5`
- **Build/Run**: `dotnet build` / `dotnet run` (from the project subdirectory)
- **Tunneling**: ngrok (installed) for exposing local endpoints — use `context7` to look up ngrok usage

## Architecture Pattern

Follow the structure from `../02_task_agent/FindHimAgent/` and `../01_task_agent/PeopleAgent/`:
- `Adapters/` — LLM client factory (OpenAI-compatible via LM Studio)
- `Config/` — configuration models bound from `appsettings.json`
- `Models/` — domain models
- `Services/` — HTTP clients for Hub API endpoints
- `Tools/` — function-calling tools exposed to the LLM agent
- `UI/` — console output via Spectre.Console
- `Program.cs` — orchestration entry point

## Key Constraints

- 100-token prompt limit (tokenizer similar to GPT-5.2 / tiktoken) — use tiktokenizer to verify
- 1.5 PP budget for all 10 queries — leverage prompt caching by keeping the prompt prefix static and placing variable data (id, description) at the end
- Iterative agent approach: the agent should run full cycles (reset → fetch CSV → 10 queries), analyze hub error responses, and refine the prompt automatically until the flag is obtained
- English prompts are more token-efficient than Polish

## Custom Commands

- `/plan` — systematic implementation planning with checkpoints and documentation
- `/implement` — step-by-step implementation of a previously created plan
