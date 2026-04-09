# GoingThere — Rocket Navigation Agent

A C# agent using the Microsoft Agent Framework that navigates a rocket through a 3×12 grid, avoiding rocks and OKO radar traps, to reach the base in column 12 and retrieve the mission flag.

## Task

The agent must:
1. Start a new game session via API
2. For each column, execute this loop:
   - Check the OKO frequency scanner for active radar traps
   - If trapped: parse the (possibly corrupted) scanner response, compute `SHA1(detectionCode + "disarm")`, and disarm the trap
   - Fetch a radio hint describing where the rock is in the next column
   - Interpret the hint (including nautical language) and move the rocket (`go`/`left`/`right`)
3. Reach column 12 and report the flag

## Architecture

```
GoingThere/
├── Program.cs                  # Entry point, DI wiring, system prompt
├── Config/AgentConfig.cs       # AgentConfig, HubConfig, TelemetryConfig
├── Adapters/OpenAiClientFactory.cs  # IChatClient factory (lmstudio/openai)
├── Services/
│   ├── AgentOrchestrator.cs    # Iterative LLM tool-call loop (max 50 iterations)
│   ├── HubApiClient.cs         # HTTP client: game API + scanner + hints, with retry/backoff
│   └── RunLogger.cs            # Timestamped file logger (logs/)
├── Tools/GameTools.cs          # 4 LLM tools: StartGame, CheckRadar, GetRadioHint, MoveRocket
├── Telemetry/TelemetrySetup.cs # OpenTelemetry OTLP export
└── UI/ConsoleUI.cs             # Spectre.Console rich output

GoingThere.AppHost/             # .NET Aspire host for observability dashboard
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [LM Studio](https://lmstudio.ai/) running at `http://localhost:1234/v1` with model `qwen3-coder-30b-a3b-instruct-mlx`
- API key for `<hub_url>`

## Setup

Create a `.env` file in the project root:

```
Hub__ApiUrl=https://<hub_url>
Hub__ApiKey=<your-apikey>
```

## Running

```bash
# Run the agent directly
dotnet run --project GoingThere

# Run with Aspire observability dashboard (traces, metrics)
dotnet run --project GoingThere.AppHost
```

## Log Files

Each run creates a timestamped log file in `logs/YYYY-MM-DD_HH-mm-ss.log` containing every LLM request/response, tool call, and API exchange — nothing truncated.

## Console Output

The agent uses Spectre.Console for rich formatted output:
- Blue panels: LLM responses
- Yellow rules: tool calls with arguments
- Green panels: successful tool results
- Red panels: errors
- Cyan rules: API calls in progress

## Observability

When running via `GoingThere.AppHost`, the Aspire dashboard is available at `http://localhost:15888` (or similar port shown at startup). It shows OpenTelemetry traces for every LLM call and HTTP request.

Set `OTEL_EXPORTER_OTLP_ENDPOINT` or `Telemetry:OtlpEndpoint` in config to point at a custom OTLP collector.

## Troubleshooting

| Issue | Solution |
|-------|----------|
| `Hub__ApiUrl is empty` | Create `.env` with `Hub__ApiUrl=https://<hub_url>` |
| LLM fails to respond | Ensure LM Studio is running and the model is loaded |
| Rocket crashes repeatedly | Check the log file for hint interpretation — the system prompt may need tuning |
| Scanner parse error | The API returned a response with no recognizable `frequency`/`detectionCode` — check logs |
