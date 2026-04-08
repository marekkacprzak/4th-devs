# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Task Overview

**Task name:** `phonecall`

A multi-step phone call agent that conducts a spoken Polish-language conversation with a system operator to:
1. Introduce as "Tymon Gajewski"
2. Ask about road status for RD224, RD472, and RD820 (mentioning transport to a Zygfryd base)
3. Request deactivation of monitoring on passable roads (citing secret food transport to Zygfryd's base)
4. Use operator password `BARBAKAN` when needed

Each conversation turn is an MP3 audio clip encoded as base64, sent to `https://<hub_url>/verify`. If the conversation goes wrong, restart with `action: "start"`.

**API flow:**
- Start: `{ "apikey": "...", "task": "phonecall", "answer": { "action": "start" } }`
- Each subsequent turn: `{ "apikey": "...", "task": "phonecall", "answer": { "audio": "<base64-mp3>" } }`
- Operator responses come back as base64 audio

## Commands

```bash
# Build
dotnet build

# Run the agent
dotnet run --project Phonecall

# Run with Aspire observability dashboard
dotnet run --project Phonecall.AppHost

# Restore packages
dotnet restore
```

Environment variables are loaded from `.env` in the project root (key=value per line), overriding `appsettings.json`. The `.env` loader searches up the directory tree, so `dotnet run` from the solution root also works.

## Architecture

Two projects:

- **Phonecall/** — main agent (ASP.NET Web, net10.0)
- **Phonecall.AppHost/** — .NET Aspire host for OpenTelemetry observability, port 5030

### Key components

| Component | Location | Purpose |
|-----------|----------|---------|
| `ConversationOrchestrator` | `Services/` | Scripted 3-step conversation state machine with 3-attempt retry; password detection via keywords; road ID extraction via regex then local LLM |
| `GeminiAudioService` | `Services/` | TTS via Gemini REST API → raw PCM → NAudio.Lame MP3; STT via Gemini multimodal inline_data |
| `CentralaApiClient` | `Services/` | `StartSessionAsync()`, `SendAudioAsync(base64)`, `VerifyAsync(answer)`; full retry + rate-limit handling |
| `OpenAiClientFactory` | `Adapters/` | Wraps `OpenAIClient` for `openai` or `lmstudio` providers with optional `UseOpenTelemetry()` |
| `TelemetrySetup` | `Telemetry/` | Builds `TracerProvider` + `MeterProvider`; exports to OTLP; traces `Phonecall.Centrala`, `Microsoft.Extensions.AI` |
| `RunLogger` | `Services/` | Timestamped structured log file: `logs/YYYY-MM-DD_HH-mm-ss.log` |

### Audio pipeline

- **TTS**: `POST https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-preview-tts:generateContent`
  - Returns raw PCM (24kHz, 16-bit, mono) in `candidates[0].content.parts[0].inlineData.data`
  - Converted to MP3 using `NAudio.Lame` (`LameMP3FileWriter` with `WaveFormat(24000, 16, 1)`)
- **STT**: `POST https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent`
  - Audio sent as `inline_data` with `mime_type` and base64 `data`
  - Transcription extracted from `candidates[0].content.parts[0].text`

### Configuration structure

```json
{
  "Agent": { "Provider": "lmstudio", "Model": "qwen3-coder-30b-a3b-instruct-mlx", "Endpoint": "http://localhost:1234/v1" },
  "Hub": { "ApiUrl": "https://<hub_url>", "ApiKey": "<from .env>", "TaskName": "phonecall" },
  "Audio": { "TtsModel": "gemini-2.5-flash-preview-tts", "TtsVoice": "Kore", "SttModel": "gemini-2.5-flash" },
  "Telemetry": { "Enabled": true, "ServiceName": "Phonecall", "OtlpEndpoint": "http://localhost:4317" }
}
```

`.env` file keys:
- `Hub__ApiKey`, `Hub__ApiUrl`
- `GEMINI_API_KEY` (for TTS + STT)

## Custom Commands

- `/plan` — systematic planning with problem analysis and saved plan in `./details/YYYY/MM/DD-topic-plan.md`
- `/implement` — step-by-step implementation with task tracking and mandatory updates to `TODO.md` and `CHANGELOG.md`

## Prerequisites

- .NET 10 SDK
- LM Studio running at `http://localhost:1234/v1` with `qwen3-coder-30b-a3b-instruct-mlx` (used for road ID extraction fallback)
- Google Gemini API key in `.env` as `GEMINI_API_KEY`
