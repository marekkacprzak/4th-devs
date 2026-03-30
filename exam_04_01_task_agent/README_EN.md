# OkoEditor Agent

A C# .NET 10 AI agent that autonomously modifies records in the OKO surveillance system (Centrum Operacyjne) via its `/verify` API. Built as part of the AI Devs 4 course.

## What it does

The agent performs three specific modifications and then confirms them:

1. **Skolwin classification** — changes the Skolwin city incident report from vehicles/people → animals
2. **Skolwin task** — marks the Skolwin task as done, noting animals (e.g., beavers) were spotted
3. **Komarowo incident** — creates a new incident report about human movement near the unpopulated city Komarowo
4. **Confirm** — calls action `done` to commit all changes

All modifications go through the `/verify` API only. The web UI at `https://<oko_url>/` is used read-only for exploration.

## Architecture

```
OkoEditor/                     ← main agent (ASP.NET Web SDK, console-driven)
  Program.cs                   ← entry point: loads config, builds services, runs agent loop
  Adapters/OpenAiClientFactory ← builds IChatClient for lmstudio or openai providers
  Config/AgentConfig.cs        ← AgentConfig, HubConfig, OkoConfig
  Config/TelemetryConfig.cs
  Models/VerifyRequest.cs      ← /verify API request DTO
  Services/AgentOrchestrator   ← iterative tool-calling loop (max 15 iterations)
  Services/CentralaApiClient   ← HTTP client for /verify with retry + rate-limit handling
  Services/RunLogger.cs        ← file logger: writes all requests/responses to logs/
  Telemetry/TelemetrySetup     ← OpenTelemetry TracerProvider + MeterProvider → OTLP
  Tools/OkoTools.cs            ← CallVerifyApi + FetchOkoPage tools
  UI/ConsoleUI.cs              ← Spectre.Console output helpers

OkoEditor.AppHost/             ← .NET Aspire host for dashboard + OTLP observability
```

The agent loop (`AgentOrchestrator.RunAgentAsync`):
1. Sends system prompt + user goal to the LLM
2. LLM calls tools (`CallVerifyApi`, `FetchOkoPage`) to explore the API and make changes
3. Tool results are appended to the conversation and the LLM continues
4. Loop ends when the LLM returns a final text response with no tool calls

## Prerequisites

- **.NET 10 SDK** — `dotnet --version` should show `10.x`
- **LM Studio** running at `http://localhost:1234/v1` with model `qwen3-coder-30b-a3b-instruct-mlx` loaded
- **Centrala API key** — your personal `apikey` for the AI Devs 4 course

## Setup

Create `OkoEditor/.env` (gitignored):

```env
Hub__ApiUrl=https://<centrala-base-url>
Hub__ApiKey=<your-apikey>
```

Optionally override the LLM model in the same file:

```env
Agent__Model=qwen3-coder-30b-a3b-instruct-mlx
Agent__Endpoint=http://localhost:1234/v1
```

## Running

```bash
# Standalone (no Aspire dashboard)
dotnet run --project OkoEditor

# With Aspire dashboard + OpenTelemetry traces
dotnet run --project OkoEditor.AppHost
```

## Log files

Every run writes a plain-text log file to `OkoEditor/logs/YYYY-MM-DD_HH-mm-ss.log`. The file path is printed at startup. Logs contain **full, untruncated** request and response bodies — nothing is omitted.

Each entry follows this format:

```
[HH:mm:ss.fff] [TAG]
<content>
────────────────────────────────────────────────────────────────────────────────
```

Tags used:

| Tag | Meaning |
|-----|---------|
| `SESSION_START` | Run start timestamp |
| `LLM_REQUEST` | Before each LLM call: iteration number and message count |
| `LLM_RESPONSE` | Raw LLM response text (including `<think>` tokens) |
| `TOOL_CALL` | Tool name, call ID, and full arguments JSON |
| `TOOL_RESULT` | Full tool return value |
| `TOOL_ERROR` | Tool exception with stack trace |
| `API_REQUEST` | Full POST URL and JSON body sent to `/verify` |
| `API_RESPONSE_OK` | HTTP 2xx status + full response body |
| `API_RESPONSE_ERROR` | HTTP 4xx/5xx status + full response body |
| `NETWORK_ERROR` | Connection-level exception with full stack trace |
| `ERROR` | LLM or other unexpected error |
| `INFO` | Informational entries (run complete, iteration counts) |

## Console output

Each run prints colour-coded output via Spectre.Console:

| Colour | Meaning |
|--------|---------|
| Blue   | LLM call start (iteration N/15, message count) |
| Blue panel | Raw LLM response text (before stripping `<think>` tokens) |
| Yellow rule | Tool call with name and call ID |
| Yellow/dim | Tool call arguments (full JSON) |
| Green panel | Tool result |
| Red panel | Tool error or failed HTTP response (full body, never truncated) |
| Dim | HTTP request body sent to `/verify` |
| Green/red `(NNN)` | HTTP response status + body (full on errors) |
| Cyan rule | Named step (`CallVerifyApi: action=help`, `POST /verify`, etc.) |
| Green panel | Final agent result |

## Troubleshooting

**`Hub__ApiUrl` is empty** — the agent will print a POST URL of `/verify` and fail with a network error. Set the URL in `.env`.

**LLM returns no tool calls on first iteration** — the model may not support function calling well. Try a different model or check that LM Studio is using the correct model with tool-calling support.

**`additionalFieldsJson` parse error** — logged as a red panel with the full parse exception. The tool falls back to calling the action without extra fields. Check what JSON the LLM is generating in the yellow tool-call args output.

**HTTP 4xx/5xx from `/verify`** — the full response body is always shown in a red panel (never truncated) and written to the log file with tag `API_RESPONSE_ERROR`.

**Max iterations reached** — increase `MaxIterations` in `AgentOrchestrator.cs` if the model needs more steps. Default is 15.
