# Plan Wdrożenia: SavethemAgent (savethem)

## Data: 2026-03-27

## Problem do rozwiązania

Zbudować agenta C# opartego na Microsoft Agent Framework, który dynamicznie odkrywa narzędzia przez API `toolsearch`, pobiera mapę 10x10, dane pojazdów i reguły terenu, a następnie wyznacza optymalną trasę do miasta Skolwin przy ograniczonych zasobach (10 jedzenia, 10 paliwa). Odpowiedź wysyłana do `/verify` w formacie `["vehicle_name", "right", "down", ...]`.

## Wybrane rozwiązanie: Opcja C — Tool-Guided Planning

Agent LLM wywołuje sekwencję AI Tools do odkrycia danych, a deterministyczny planer trasy (BFS + resource calculator) jest wyeksponowany jako AI Tool `PlanOptimalRoute`. Flow:

```
SearchTools() → GetMap() → GetVehicles() → GetMovementRules() → PlanOptimalRoute() → SubmitSolution()
```

Wzorzec implementacji: `exam_03_03_task_agent` (ReactorAgent).

---

## Plan implementacji

### Faza 1: Scaffold projektu

**1.1 Utwórz solution i projekty**
```bash
dotnet new sln -n savethem
dotnet new console -n SavethemAgent --framework net10.0
dotnet new aspireapp -n SavethemAgent.AppHost
dotnet sln add SavethemAgent/SavethemAgent.csproj
dotnet sln add SavethemAgent.AppHost/SavethemAgent.AppHost.csproj
```

**1.2 Dodaj NuGet packages do `SavethemAgent.csproj`**
```xml
<PackageReference Include="Microsoft.Agents.AI.OpenAI" Version="1.0.0-rc4" />
<PackageReference Include="Microsoft.Extensions.Configuration.Binder" Version="10.0.5" />
<PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="10.0.5" />
<PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.5" />
<PackageReference Include="OpenTelemetry" Version="1.*" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.*" />
<PackageReference Include="Spectre.Console" Version="0.54.0" />
```

**1.3 Dodaj AppHost reference do `SavethemAgent.AppHost.csproj`**
- SDK: `Aspire.AppHost.Sdk/13.1.2`
- ProjectReference do SavethemAgent

**1.4 Aktualizuj `.aspire/settings.json`**
```json
{ "appHostPath": "../SavethemAgent.AppHost/SavethemAgent.AppHost.csproj" }
```

---

### Faza 2: Warstwa konfiguracji (`Config/`)

**2.1 `AgentConfig.cs`**
```csharp
public class AgentConfig {
    public string Provider { get; set; } = "lmstudio";
    public string Model { get; set; } = "qwen3-coder-30b-a3b-instruct-mlx";
    public string Endpoint { get; set; } = "http://localhost:1234/v1";
    public string ApiKey { get; set; } = "";
    public string GetApiKey() => !string.IsNullOrEmpty(ApiKey) ? ApiKey
        : Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "lm-studio";
}
```

**2.2 `HubConfig.cs`**
```csharp
public class HubConfig {
    public string ToolSearchUrl { get; set; } = "https://<hub_api>/api/toolsearch";
    public string VerifyUrl { get; set; } = "https://<hub_api>/verify";
    public string ApiKey { get; set; } = "";
    public string TaskName { get; set; } = "savethem";
    public int MaxRetries { get; set; } = 5;
    public int RetryDelayMs { get; set; } = 500;
}
```

**2.3 `TelemetryConfig.cs`** — kopia z ReactorAgent, zmień ServiceName na `"SavethemAgent"`

**2.4 `appsettings.json`**
```json
{
  "Agent": { "Provider": "lmstudio", "Model": "qwen3-coder-30b-a3b-instruct-mlx", "Endpoint": "http://localhost:1234/v1" },
  "Hub": { "ToolSearchUrl": "https://<hub_api>/api/toolsearch", "VerifyUrl": "https://<hub_api>/verify", "ApiKey": "", "TaskName": "savethem" },
  "Telemetry": { "Enabled": true, "OtlpEndpoint": "http://localhost:4317", "ServiceName": "SavethemAgent", "EnableSensitiveData": true }
}
```

---

### Faza 3: Modele domenowe (`Models/`)

**3.1 `GridMap.cs`** — 10x10 siatka z przeszkodami
```csharp
public class GridMap {
    public int Width { get; } = 10;
    public int Height { get; } = 10;
    public Cell[,] Cells { get; set; }        // Walkable, Obstacle, Start, Goal
    public (int x, int y) StartPosition { get; set; }
    public (int x, int y) GoalPosition { get; set; }   // Skolwin
    public string ToTextGrid() { ... }
}

public enum CellType { Walkable, Obstacle, Start, Goal }
```

**3.2 `Vehicle.cs`**
```csharp
public class Vehicle {
    public string Name { get; set; }
    public double FuelPerStep { get; set; }   // units of fuel consumed per move
    public int StepsPerMove { get; set; }     // how many grid cells moved per "step"
}
```

**3.3 `RouteResult.cs`**
```csharp
public class RouteResult {
    public string VehicleName { get; set; }
    public List<string> Moves { get; set; }   // ["right", "down", ...]
    public double TotalFuel { get; set; }
    public int TotalFood { get; set; }
    public bool IsValid { get; set; }
    public string[] ToAnswer() => new[] { VehicleName }.Concat(Moves).ToArray();
}
```

---

### Faza 4: Warstwa serwisów (`Services/`)

**4.1 `RequestLogger.cs`** ⚠️ WYMAGANIE z task.md: logowanie do pliku WSZYSTKICH request/response
- Kopia z ReactorAgent, zmień tylko nazwę serwisu w nagłówku logu
- Loguje: wszystkie HTTP requesty do toolsearch, do każdego narzędzia, do /verify
- Format: timestamp + metoda + URL + body requesta / status + body odpowiedzi
- Plik tworzony w `logs/savethem-YYYY-MM-DD-HH-mm-ss.log`
- Thread-safe (lock na StreamWriter)

**4.2 `HubApiClient.cs`** — adaptacja z ReactorAgent
- Metoda `ToolSearchAsync(string query)` → POST do `ToolSearchUrl`
- Metoda `CallToolAsync(string toolUrl, string query)` → POST do dowolnego narzędzia
- Metoda `VerifyAsync(string[] answer)` → POST do `VerifyUrl` z `{ task, answer }`
- Pełna obsługa retry + rate limiting (jak w ReactorAgent)

**4.3 `MapParser.cs`** — parsowanie odpowiedzi API do `GridMap`
- Obsługa różnych formatów JSON (camelCase, snake_case)
- Interpretacja symboli terenu: river/water → Obstacle, tree → Obstacle, stone → Obstacle

**4.4 `RoutePlanner.cs`** — deterministyczny BFS + resource optimizer
```
Algorytm:
1. Dla każdego pojazdu (+ opcja pieszo):
   a. BFS z wagami: znajdź najkrótszą ścieżkę omijającą przeszkody
   b. Oblicz koszt zasobów: fuel = steps * vehicle.FuelPerStep, food = steps
   c. Sprawdź czy zasoby wystarczają (fuel ≤ 10, food ≤ 10)
2. Wybierz pojazd z największym marginesem bezpieczeństwa
3. Zwróć RouteResult z listą ruchów
```

---

### Faza 5: Adaptery (`Adapters/`)

**5.1 `OpenAiClientFactory.cs`** — kopia z ReactorAgent, zmień tylko nazwy aktywności OTLP

**5.2 `LoggingChatClient.cs`** — kopia bezpośrednia z ReactorAgent

---

### Faza 6: Definicje narzędzi AI (`Tools/`)

**6.1 `SavethemTools.cs`** — 6 AI Functions:

```csharp
// Tool 1: Odkrywanie narzędzi
[Description("Search for available tools using the tool search API. Query in English.")]
async Task<string> SearchAvailableTools(string query)
→ wywołuje HubApiClient.ToolSearchAsync(query)
→ zwraca JSON z top-3 narzędziami (name, url, description)

// Tool 2: Pobieranie mapy
[Description("Call a discovered tool to get the terrain map. Pass the tool URL and query.")]
async Task<string> CallTool(string toolUrl, string query)
→ wywołuje HubApiClient.CallToolAsync(toolUrl, query)
→ zwraca surową odpowiedź JSON

// Tool 3: Parsowanie i przechowywanie mapy
[Description("Parse and store the map from tool response JSON. Returns map summary.")]
string ParseAndStoreMap(string mapJson)
→ wywołuje MapParser.Parse(mapJson)
→ zapisuje do _currentMap
→ zwraca podsumowanie: "10x10 map, start at (0,0), goal at (9,9), 15 obstacles"

// Tool 4: Parsowanie i przechowywanie pojazdów
[Description("Parse and store vehicle data from tool response JSON. Returns vehicle list.")]
string ParseAndStoreVehicles(string vehiclesJson)
→ parsuje listę pojazdów do _vehicles
→ zwraca tabelę: "vehicle1: 0.5 fuel/step, vehicle2: 1.0 fuel/step, ..."

// Tool 5: Planowanie trasy
[Description("Calculate optimal route using BFS pathfinding and resource optimization. Returns move list.")]
string PlanOptimalRoute()
→ wywołuje RoutePlanner.Plan(_currentMap, _vehicles)
→ zapisuje do _currentRoute
→ zwraca: "Route planned: vehicle1, 18 moves, 9 fuel, 18 food"

// Tool 6: Wysyłanie rozwiązania
[Description("Submit the planned route to the verification endpoint.")]
async Task<string> SubmitSolution()
→ wywołuje HubApiClient.VerifyAsync(_currentRoute.ToAnswer())
→ zwraca odpowiedź API (sukces/błąd + flaga)
```

---

### Faza 7: Telemetria (`Telemetry/`)

**7.1 `TelemetrySetup.cs`** — adaptacja z ReactorAgent
- ActivitySources: `"SavethemAgent"`, `"SavethemAgent.Hub"`, `"SavethemAgent.Planner"`, `"Microsoft.Extensions.AI"`, `"Microsoft.Agents.AI"`

---

### Faza 8: UI (`UI/`)

**8.1 `ConsoleUI.cs`** — kopia z ReactorAgent, dostosuj teksty i kolory do tematu "savethem"

---

### Faza 9: Główny program (`Program.cs`)

```
Faza 1: Wczytaj .env (ręczne parsowanie key=value)
Faza 2: Konfiguracja (ConfigurationBuilder + bind do typed config)
Faza 3: Utwórz serwisy (HttpClient, HubApiClient, MapParser, RoutePlanner, SavethemTools)
Faza 4: Zbuduj chat client (OpenAiClientFactory, LoggingChatClient, TelemetrySetup)
Faza 5: Utwórz AIAgent z narzędziami
Faza 6: Uruchom agenta z promptem systemowym
Faza 7: Fallback deterministyczny (jeśli agent nie wysłał rozwiązania)
```

**Prompt systemowy dla agenta:**
```
You are a route planning agent. Your mission:
1. Search for tools: query "map terrain grid" and "vehicle transport fuel"
2. Call discovered tools to get the map and vehicle data
3. Parse and store the map and vehicles
4. Plan the optimal route using PlanOptimalRoute tool
5. Submit the solution using SubmitSolution tool

You have 10 food rations and 10 fuel units. Reach city Skolwin (goal on the map).
All tool queries must be in English.
```

---

### Faza 10: AppHost (`SavethemAgent.AppHost/`)

**10.1 `AppHost.cs`**
```csharp
var builder = DistributedApplication.CreateBuilder(args);
builder.AddProject<Projects.SavethemAgent>("savethem-agent");
builder.Build().Run();
```

---

### Faza 11: Dokumentacja

**11.1 `readme_en.md`** — English README (wymagany przez task.md)
- Opis zadania, uruchamianie, konfiguracja, architektura

---

## Parametry rozwiązania

| Parametr | Wartość |
|----------|---------|
| Framework | Microsoft Agent Framework (Microsoft.Agents.AI.OpenAI 1.0.0-rc4) |
| LLM | LM Studio lokalny, `qwen3-coder-30b-a3b-instruct-mlx` |
| Telemetria | Aspire AppHost + OTLP → `http://localhost:4317` |
| Algorytm trasy | BFS + resource constraint check |
| Logowanie HTTP | ⚠️ RequestLogger — WSZYSTKIE req/res do pliku `logs/savethem-*.log` |
| Logowanie LLM | LoggingChatClient — wszystkie kroki LLM do pliku |
| Fallback | Deterministyczne wywołanie RoutePlanner bez LLM |

## Oczekiwane rezultaty

- Agent samodzielnie odkrywa narzędzia, pobiera mapę i pojazdy, wyznacza trasę i wysyła odpowiedź
- Optymalna trasa mieści się w limicie 10 jedzenia i 10 paliwa
- Wszystkie HTTP request/response logowane do pliku w `logs/`
- Traces widoczne w dashboardzie Aspire
- Flaga zwrócona z `/verify` po poprawnym rozwiązaniu
