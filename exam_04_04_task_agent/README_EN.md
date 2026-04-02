# Filesystem — Trade Notes Agent

AI Devs 4 task agent that parses Natan's chaotic trade notes and organizes them into a structured virtual filesystem via the Centrala hub API.

## What It Does

1. Downloads Natan's trade notes archive from the hub
2. Uses an LLM to extract structured trade data (cities, people, goods) from chaotic Polish-language notes
3. Constructs a virtual filesystem with three directories via batch API
4. Submits the completed filesystem for verification

## Architecture

```
Filesystem/                     ← Main agent (console app)
  Program.cs                    ← Entry point, wiring
  Config/                       ← AgentConfig, HubConfig, TelemetryConfig
  Models/                       ← VerifyRequest, TradeCity/TradePerson/TradeGood DTOs
  Adapters/                     ← OpenAiClientFactory (openai / lmstudio)
  Services/
    CentralaApiClient.cs        ← HTTP client with retry, rate-limit, telemetry
    FilesystemOrchestrator.cs   ← Core hybrid orchestration logic
    RunLogger.cs                ← Structured file logging
  Telemetry/TelemetrySetup.cs  ← OpenTelemetry TracerProvider + MeterProvider → OTLP
  Tools/FilesystemTools.cs     ← API action wrappers
  UI/ConsoleUI.cs              ← Spectre.Console output helpers

Filesystem.AppHost/             ← .NET Aspire observability dashboard
```

### Orchestration Flow

```
Phase 0: Discovery & Reset
Phase 1: Download natan_notes.zip → extract all text files
Phase 2: LLM extracts cities/people/goods from Polish notes → TradeData JSON
Phase 3: Build filesystem via single batch API call
Phase 4: Verify listing → done()
```

## Filesystem Structure

```
/miasta/{city}    — JSON: {"good": quantity, ...}   (goods needed by city)
/osoby/{person}   — "First Last\n[City](../miasta/city)"
/towary/{good}    — "[City](../miasta/city)"
```

Rules: no Polish characters in file names or JSON keys; goods in singular nominative form.

## Configuration

`.env` (project root):
```
Hub__ApiKey=<your-api-key>
Hub__ApiUrl=https://<hub_url>
Hub__VerifyUrl=https://<hub_url>/verify
```

`Filesystem/appsettings.json` — LLM provider, model, telemetry settings.

## Running

```bash
# Run the agent
dotnet run --project Filesystem

# Run with Aspire observability dashboard
dotnet run --project Filesystem.AppHost

# Build only
dotnet build
```

Requires LM Studio running at `http://localhost:1234/v1` with model `qwen3-coder-30b-a3b-instruct-mlx`.

## Logs

Structured run logs are written to `Filesystem/logs/YYYY-MM-DD_HH-mm-ss.log`. Each entry has a tag (`LLM_REQUEST`, `LLM_RESPONSE`, `API_REQUEST`, `API_RESPONSE_OK`, `PHASE`, etc.) and full content.
