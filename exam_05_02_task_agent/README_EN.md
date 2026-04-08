# Phonecall Agent

A C# .NET 10 agent that conducts a scripted Polish-language phone conversation with a system operator to determine which roads are safe for evacuation transport and disable monitoring on those roads.

## Task

**Task name:** `phonecall`
**Endpoint:** `https://<hub_url>/verify`

The agent impersonates Tymon Gajewski and must:
1. Introduce itself
2. Ask about road status for RD224, RD472, and RD820 (citing transport to a Zygfryd base)
3. Request monitoring deactivation on passable roads (citing secret food transport)
4. Use password `BARBAKAN` if challenged

## Architecture

Two projects:

```
Phonecall/                    — Main agent (ASP.NET Web, net10.0)
  Config/
    AgentConfig.cs            — LLM config (lmstudio/openai)
    AudioConfig.cs            — Gemini TTS/STT config
    HubConfig.cs              — Centrala API config
    TelemetryConfig.cs        — OTLP config
  Adapters/
    OpenAiClientFactory.cs    — IChatClient factory
  Services/
    ConversationOrchestrator.cs  — Scripted 3-step conversation state machine
    GeminiAudioService.cs        — TTS (Gemini → PCM → MP3 via NAudio.Lame) + STT
    CentralaApiClient.cs         — POST /verify with retry/rate-limit
    RunLogger.cs                 — Timestamped file logger → logs/YYYY-MM-DD_HH-mm-ss.log
  Telemetry/
    TelemetrySetup.cs         — OpenTelemetry (traces + metrics → OTLP)
  UI/
    ConsoleUI.cs              — Spectre.Console rich output

Phonecall.AppHost/            — .NET Aspire host for observability dashboard
```

### Conversation Flow

```
START session (action: "start")
  │
  ├── STEP 1: "Dzień dobry, nazywam się Tymon Gajewski."
  │     ← operator response (STT)
  │
  ├── STEP 2: Ask about RD224, RD472, RD820 (mention Zygfryd transport)
  │     ← operator response (STT) → LLM extracts passable road IDs
  │
  ├── STEP 3: Request monitoring deactivation on passable road(s)
  │     ← operator response → check for flag
  │
  └── [PASSWORD?] → "Hasło to BARBAKAN." at any step if operator asks
```

### Audio Pipeline

- **TTS**: Gemini `gemini-2.5-flash-preview-tts` → raw PCM (24kHz/16-bit/mono) → NAudio.Lame → MP3 → base64
- **STT**: Gemini `gemini-2.5-flash` multimodal (inline_data) → Polish text transcript
- **Road extraction**: Regex first, then local LM Studio LLM as fallback

## Prerequisites

- .NET 10 SDK
- LM Studio running at `http://localhost:1234/v1` with `qwen3-coder-30b-a3b-instruct-mlx`
- Google Gemini API key (set `GEMINI_API_KEY` in `.env`)

## Setup

Create or update `.env` in the project root:

```
Hub__ApiKey=<your-aidevs-apikey>
Hub__ApiUrl=https://<hub_url>
GEMINI_API_KEY=<your-gemini-api-key>
```

## Running

```bash
# Run the agent directly
dotnet run --project Phonecall

# Run with Aspire observability dashboard (OTLP traces)
dotnet run --project Phonecall.AppHost

# Build only
dotnet build
```

## Logs

Every run creates a timestamped log file at `Phonecall/logs/YYYY-MM-DD_HH-mm-ss.log` with:
- Full operator transcripts
- TTS/STT requests
- API request/response bodies
- LLM road extraction

Use logs to debug conversation failures and adjust the scripted messages.

## Restarting a Failed Conversation

If the conversation fails (wrong sequence, timeout), the agent automatically restarts with a new `action: "start"` call (up to 3 attempts). Check the log for the operator's exact words to tune the scripted messages.
