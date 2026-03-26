# ReactorAgent — negotiations

AI Devs 4 course task. Exposes two HTTP tool endpoints that an autonomous agent uses to find survivor cities selling wind turbine components.

## How it works

The external agent at `<hub_api>` POSTs natural-language queries to one of two endpoints:

```json
POST /api/search
{ "params": "turbina wiatrowa" }

→ { "output": "Domatowo, Rzeszow, Skolwin" }
```

```json
POST /api/items
{ "params": "" }

→ { "output": "akumulator 100Ah, generator 5kW, kabel 10m, ..." }
```

`/api/search` matches the query to items in a CSV knowledge base using keyword pre-filtering + a local LLM (Qwen via LM Studio), then returns the cities that sell the matched item.

`/api/items` returns the full list of available item names — useful when the agent needs to browse the catalog before searching.

## Data model

Three CSVs at `https://<hub_api>/`dane/s03e04_csv/`:

| File | Columns | Description |
|------|---------|-------------|
| `cities.csv` | name, code | ~51 survivor cities |
| `items.csv` | name, code | ~2136 items for sale |
| `connections.csv` | itemCode, cityCode | ~4000 item↔city mappings |

## Stack

- **.NET 10** — ASP.NET Core Minimal APIs
- **Microsoft.Agents.AI.OpenAI** — LLM client
- **LM Studio** — local LLM at `http://localhost:1234/v1`, model `qwen3-coder-30b-a3b-instruct-mlx`
- **.NET Aspire** — distributed app host with observability dashboard
- **OpenTelemetry** — traces and metrics via OTLP
- **Spectre.Console** — rich terminal output

## Project structure

```
Negotiations/
├── Adapters/           LM Studio client factory
├── Config/             Config POCOs (Agent, Hub, Reactor, Telemetry)
├── Models/             ToolRequest / ToolResponse
├── Services/
│   ├── CsvDataService.cs       Downloads CSVs, builds in-memory indexes
│   ├── InteractionLogger.cs    File-based req/res + LLM logging
│   └── ItemMatcherService.cs   Natural-language → item code matching
├── Tools/
│   └── SearchTool.cs           Orchestrates match + lookup + response
├── Telemetry/          OpenTelemetry setup
├── UI/                 Console output
└── Program.cs          Entry point, endpoint wiring
Negotiations.AppHost/   Aspire host
```

## Configuration

All Hub config is stored in `.env` next to `appsettings.json`:

```
Hub__ApiKey=your-key-here
Hub__ApiUrl=https://<hub_api>/verify
Hub__TaskName=negotiations
Hub__CsvBaseUrl=https://<hub_api>/dane/s03e04_csv/
```

`appsettings.json` holds non-sensitive config:

```json
{
  "Agent":     { "Provider": "lmstudio", "Model": "qwen3-coder-30b-a3b-instruct-mlx", "Endpoint": "http://localhost:1234/v1" },
  "Reactor":   { "Port": 3000 },
  "Telemetry": { "Enabled": true, "OtlpEndpoint": "http://localhost:4317" }
}
```

## Run

```bash
# Standard run
dotnet run --project Negotiations

# With Aspire dashboard (http://localhost:15888)
dotnet run --project Negotiations.AppHost
```

Logs are written to `Negotiations/logs/interactions_TIMESTAMP.log`.

## Submit to hub

```bash
# 1. Expose publicly
ngrok http 3000

# 2. Register tools
curl -X POST https://<hub_api>/verify \
  -H "Content-Type: application/json" \
  -d '{
    "apikey": "098c468b-70f4-4623-9d7a-fffd78149198",
    "task": "negotiations",
    "answer": {
      "tools": [
        {
          "URL": "https://unimminent-vasiliki-urethral.ngrok-free.dev/api/search",
          "description": "Wyszukaj miasta sprzedające podany przedmiot. Przekaż w polu params nazwę lub opis przedmiotu (np. turbina wiatrowa, kabel 10m). Zwraca listę miast oferujących ten przedmiot."
        },
        {
          "URL": "https://<ngrok-domain>/api/items",
          "description": "Zwraca listę wszystkich dostępnych przedmiotów w bazie. Nie wymaga parametrów (przekaż dowolny tekst w params). Użyj gdy nie znasz dokładnej nazwy przedmiotu."
        }
      ]
    }
  }'

# 3. Check result after 30–60 seconds
curl -X POST https://<hub_api>/verify \
  -H "Content-Type: application/json" \
  -d '{"apikey":"<your-key>","task":"negotiations","answer":{"action":"check"}}'
```

Debug panel: `https://<hub_api>/debug`
