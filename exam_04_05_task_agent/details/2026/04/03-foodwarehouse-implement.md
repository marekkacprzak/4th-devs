# Dokumentacja Wdrożenia FoodWareHouse Agent

## Data: 2026-04-03

## Środowisko
- .NET 10, C#
- Microsoft.Agents.AI.OpenAI 1.0.0-rc4
- .NET Aspire AppHost SDK 13.1.2
- LM Studio (qwen3-coder-30b-a3b-instruct-mlx @ localhost:1234)
- OpenTelemetry OTLP → localhost:4317

## Konfiguracja wykonana

### Krok 1: Scaffolding ✅
```
FoodWareHouse/FoodWareHouse.csproj        (net10.0, Sdk.Web)
FoodWareHouse/appsettings.json            (Agent/Hub/Telemetry config)
FoodWareHouse/.env                        (skopiowane z root .env)
FoodWareHouse.AppHost/AppHost.cs          (port 5013)
FoodWareHouse.AppHost/FoodWareHouse.AppHost.csproj
FoodWareHouse.AppHost/appsettings.json
```
**Status**: ✅

### Krok 2: Config i Models ✅
```
FoodWareHouse/Config/AgentConfig.cs       (HubConfig z Food4CitiesUrl zamiast NotesUrl)
FoodWareHouse/Config/TelemetryConfig.cs
FoodWareHouse/Models/VerifyRequest.cs
FoodWareHouse/Models/FoodWareHouseModels.cs (CityDemand, CreatedOrder, DbUser, DbDestination)
```
**Status**: ✅

### Krok 3: Infrastruktura ✅
```
FoodWareHouse/Adapters/OpenAiClientFactory.cs
FoodWareHouse/Services/CentralaApiClient.cs   (activity source: FoodWareHouse.Centrala)
FoodWareHouse/Services/RunLogger.cs            (+ LogDbQuery, LogOrderCreated, LogOrderItems, LogSignature)
FoodWareHouse/Telemetry/TelemetrySetup.cs     (sources: FoodWareHouse + FoodWareHouse.Centrala)
FoodWareHouse/UI/ConsoleUI.cs                  (+ PrintSuccess)
```
**Status**: ✅

### Krok 4: Tools ✅
```
FoodWareHouse/Tools/FoodWareHouseTools.cs
```
Tool wrappers z "tool" field pattern (kluczowa różnica od poprzednich zadań):
- Help, Reset, Done → `{ tool: "help/reset/done" }`
- DatabaseQuery → `{ tool: "database", query: "..." }`
- OrdersGet/Create/Append → `{ tool: "orders", action: "get/create/append", ... }`
- GenerateSignature → `{ tool: "signatureGenerator", ...fields }`
- FetchCityDemands → HTTP GET (bez API)

**Status**: ✅

### Krok 5: Orchestrator ✅
```
FoodWareHouse/Services/FoodWareHouseOrchestrator.cs
```
6 faz:
- Phase 0: Help + Reset + OrdersGet
- Phase 1: Fetch food4cities.json + parse (object or array format)
- Phase 2: show tables + select all + extract destinations + creatorID
- Phase 3: GenerateSignature(fields from DB)
- Phase 4: Per city: OrdersCreate + extract orderId + OrdersAppend(batch)
- Phase 5: OrdersGet + Done

Parsowanie odpowiedzi dynamiczne z JsonDocument (format DB nieznany upfront).

**Status**: ✅

### Krok 6: Program.cs ✅
```
FoodWareHouse/Program.cs
```
Wzorzec Filesystem: .env → WebApplication → bind configs → services → orchestrator → run.

**Status**: ✅

### Krok 7: Build ✅
```bash
dotnet build FoodWareHouse/FoodWareHouse.csproj
# Build succeeded. 0 Warning(s), 0 Error(s)

dotnet build FoodWareHouse.AppHost/FoodWareHouse.AppHost.csproj
# Build succeeded. 0 Warning(s), 0 Error(s)
```
**Status**: ✅

### Krok 8: Dokumentacja ✅
```
README_EN.md
details/2026/04/03-foodwarehouse-implement.md (ten plik)
```
**Status**: ✅

## Logowanie do pliku

RunLogger zapisuje strukturyzowane logi do `FoodWareHouse/logs/YYYY-MM-DD_HH-mm-ss.log`.
Każdy wpis ma timestamp + tag:
- PHASE, API_REQUEST, API_RESPONSE_OK/ERROR, DB_QUERY
- ORDER_CREATED, ORDER_ITEMS, SIGNATURE
- INFO, ERROR, NETWORK_ERROR

## Instrukcje uruchamiania

```bash
# Standalone
cd FoodWareHouse && dotnet run

# Z dashboardem Aspire
cd FoodWareHouse.AppHost && dotnet run
```
