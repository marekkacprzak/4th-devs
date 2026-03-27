# Dokumentacja Wdrożenia: SavethemAgent

## Data: 2026-03-27

## Środowisko
- .NET 10.0
- Microsoft.Agents.AI.OpenAI 1.0.0-rc4
- Aspire.AppHost.Sdk 13.1.2
- LM Studio lokalny (http://localhost:1234/v1)

## Konfiguracja wykonana

- Hub zmienne przeniesione do `.env` (nie w `appsettings.json`)
- `.env.example` dostarcza szablon
- `appsettings.json` zawiera tylko `MaxRetries`, `RetryDelayMs`, Agent, Telemetry

---

### Krok 1: Scaffold solution
```bash
dotnet new sln -n savethem
dotnet new console -n SavethemAgent --framework net10.0
dotnet new aspire-apphost -n SavethemAgent.AppHost --output SavethemAgent.AppHost
dotnet sln savethem.slnx add SavethemAgent/SavethemAgent.csproj SavethemAgent.AppHost/SavethemAgent.AppHost.csproj
```
**Status**: ✅ (uwaga: dotnet 10 tworzy `.slnx` zamiast `.sln`)

### Krok 2: Config layer
- `Config/AgentConfig.cs` — AgentConfig + HubConfig
- `Config/TelemetryConfig.cs`
- `appsettings.json` — Hub sekcja tylko MaxRetries/RetryDelayMs

**Status**: ✅

### Krok 3: Models
- `Models/GridMap.cs` — 10x10, CellType enum, ToTextGrid()
- `Models/Vehicle.cs` — Name, FuelPerStep, FoodPerStep
- `Models/RouteResult.cs` — Moves, ToAnswer()

**Status**: ✅

### Krok 4: Services
- `Services/RequestLogger.cs` — thread-safe file logger (log/savethem_run_*.log)
- `Services/HubApiClient.cs` — ToolSearchAsync, CallToolAsync, VerifyAsync + retry + rate limiting
- `Services/MapParser.cs` — flexible JSON parser (array, text grid, object shapes)
- `Services/VehicleParser.cs` — flexible JSON vehicle parser
- `Services/RoutePlanner.cs` — BFS + resource optimizer (fuel ≤ 10, food ≤ 10)

**Status**: ✅

### Krok 5: Adapters
- `Adapters/OpenAiClientFactory.cs` — IChatClient builder (lmstudio/openai) z OTLP
- `Adapters/LoggingChatClient.cs` — DelegatingChatClient zapisujący LLM req/res do pliku

**Status**: ✅

### Krok 6: AI Tools
- `Tools/SavethemTools.cs` — 6 funkcji: SearchAvailableTools, CallTool, ParseAndStoreMap, ParseAndStoreVehicles, PlanOptimalRoute, SubmitSolution

**Status**: ✅ (fix: MapParser jest statyczną klasą, usunięto instancję pola)

### Krok 7: Telemetry + UI + Program.cs + AppHost
- `Telemetry/TelemetrySetup.cs` — TracerProvider + MeterProvider z OTLP
- `UI/ConsoleUI.cs` — Spectre.Console
- `Program.cs` — 7-fazowa orchestracja + deterministic fallback
- `SavethemAgent.AppHost/AppHost.cs` — Aspire minimal

**Status**: ✅

### Krok 8: Build i weryfikacja
```bash
dotnet build SavethemAgent/SavethemAgent.csproj
dotnet build SavethemAgent.AppHost/SavethemAgent.AppHost.csproj
```
**Status**: ✅ — Build succeeded, 0 Warnings, 0 Errors

---

## Testy i weryfikacja

- Kompilacja: ✅ obydwa projekty
- Struktury plików: ✅ wszystkie katalogi i pliki
- .env template: ✅ `.env.example`
- Logi: ✅ RequestLogger + LoggingChatClient

## Konfiguracja finalna

Plik `.env` (do uzupełnienia):
```
Hub__ApiKey=your-api-key-here
Hub__ToolSearchUrl=https://<hub_api>/api/toolsearch
Hub__VerifyUrl=https://<hub_api>/verify
Hub__TaskName=savethem
```

## Instrukcje uruchomienia

```bash
# Przygotowanie
cp SavethemAgent/.env.example SavethemAgent/.env
# Uzupełnij Hub__ApiKey w .env

# Uruchomienie bez Aspire
dotnet run --project SavethemAgent/

# Uruchomienie z Aspire (dashboard)
dotnet run --project SavethemAgent.AppHost/
```
