# FoodWareHouse — Food Distribution Agent

AI Devs 4 task agent that reprograms the food warehouse distribution system to deliver supplies to cities in need.

## What It Does

1. **Fetches city demands** from `food4cities.json` — which cities need what items and how many
2. **Explores the SQLite database** via API — discovers destination codes, creator ID, and signature data
3. **Generates SHA1 signatures** via the `signatureGenerator` API tool
4. **Creates one order per city** with the correct `creatorID`, `destination`, and `signature`
5. **Appends exact items** to each order in batch mode (no excess, no deficit)
6. **Submits for verification** via `done` — receives the flag

## Architecture

```
FoodWareHouse/                       <- Main agent (console/web app)
  Program.cs                         <- Entry point, DI wiring
  Config/
    AgentConfig.cs                   <- AgentConfig + HubConfig (with Food4CitiesUrl)
    TelemetryConfig.cs
  Models/
    VerifyRequest.cs                 <- DTO for /verify
    FoodWareHouseModels.cs           <- CityDemand, CreatedOrder, DbDestination, DbUser
  Adapters/
    OpenAiClientFactory.cs           <- IChatClient for lmstudio/openai
  Services/
    CentralaApiClient.cs             <- HTTP client with retry, rate-limit, telemetry
    FoodWareHouseOrchestrator.cs     <- Core phased logic (6 phases)
    RunLogger.cs                     <- Structured file logging → logs/YYYY-MM-DD_HH-mm-ss.log
  Tools/
    FoodWareHouseTools.cs            <- API wrappers using "tool" field pattern
  Telemetry/
    TelemetrySetup.cs                <- OpenTelemetry TracerProvider + MeterProvider → OTLP
  UI/
    ConsoleUI.cs                     <- Spectre.Console colored output

FoodWareHouse.AppHost/               <- .NET Aspire observability dashboard (port 5013)
```

## Orchestration Phases

| Phase | Name | Description |
|-------|------|-------------|
| 0 | Discovery & Reset | Call `help`, `reset`, verify initial state |
| 1 | Fetch City Demands | Download and parse `food4cities.json` |
| 2 | Database Exploration | `show tables`, query schema, extract destinations + creator |
| 3 | Generate Signatures | Call `signatureGenerator` with user data from DB |
| 4 | Create Orders & Items | `orders.create` + `orders.append` (batch) per city |
| 5 | Verify & Submit | `orders.get` final check, then `done` → flag |

## API Pattern

All calls use `tool` field (unlike previous tasks that used `action` at root level):

```json
{ "apikey": "...", "task": "foodwarehouse", "answer": { "tool": "database", "query": "show tables" } }
{ "apikey": "...", "task": "foodwarehouse", "answer": { "tool": "orders", "action": "create", "title": "...", "creatorID": 1, "destination": "1234", "signature": "abc..." } }
```

## Running

### Standalone (recommended for debugging)
```bash
cd FoodWareHouse
dotnet run
```

### With Aspire Dashboard (observability)
```bash
cd FoodWareHouse.AppHost
dotnet run
```

### Build only
```bash
dotnet build
```

### Reset task state (if something breaks)
The agent calls `reset` automatically at startup. You can also trigger it manually by running the agent — it always resets before starting work.

## Prerequisites

- **.NET 10** SDK
- **LM Studio** running at `http://localhost:1234/v1` with model `qwen3-coder-30b-a3b-instruct-mlx`
- **`.env` file** in `FoodWareHouse/` with:
  ```
  Hub__ApiKey=<your-apikey>
  Hub__ApiUrl=https://<hub_url>
  Hub__VerifyUrl=https://<hub_url>/verify
  ```

## Log Files

Structured run logs are written to `FoodWareHouse/logs/YYYY-MM-DD_HH-mm-ss.log`.

Each entry is tagged for easy analysis:

| Tag | Description |
|-----|-------------|
| `SESSION_START` | Log file opened |
| `PHASE` | Phase transition |
| `API_REQUEST` | Full request JSON sent to /verify |
| `API_RESPONSE_OK` | Successful API response (status 2xx) |
| `API_RESPONSE_ERROR` | Failed API response |
| `DB_QUERY` | Database query + result |
| `ORDER_CREATED` | Order ID assigned to a city |
| `ORDER_ITEMS` | Items appended to order |
| `SIGNATURE` | Generated SHA1 signature |
| `INFO` | General informational message |
| `ERROR` | Error with context |
| `NETWORK_ERROR` | HTTP network failure |

## Console Output Colors

- **Magenta** — Phase transitions
- **Cyan** — API call steps
- **Green** — Successes and results
- **Blue** — LLM interactions
- **Yellow** — Warnings and retries
- **Red** — Errors

## Troubleshooting

- **Missing destination codes**: Check Phase 2 logs — the DB exploration may not have found the destinations table. The agent searches for tables with "city/miasto/name" and "code/kod/destination" columns.
- **Signature error**: Check the `SIGNATURE` log entries. The `signatureGenerator` requires specific fields from the DB — see Phase 2 logs for what was discovered.
- **Order ID extraction failed**: Check `ORDER_CREATED` log entries. The response format from `orders.create` determines how the ID is extracted.
- **Wrong item quantities**: Verify `food4cities.json` parsing in Phase 1 logs (`INFO` tag after "Parsed N city demands").
