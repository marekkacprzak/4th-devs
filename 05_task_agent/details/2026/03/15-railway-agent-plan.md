# Plan Wdrożenia: Railway Agent w C# z MS Agent Framework

## Data: 2026-03-15

## Problem do rozwiązania
Aktywacja trasy kolejowej X-01 (Gdańsk-Żarnowiec) przez Railway API (`https://hub.ag3nts.org/verify`). API jest samo-dokumentujące, zwraca błędy 503 i ma restrykcyjne rate limity. Agent musi autonomicznie wykonać sekwencję: `reconfigure` → `setstatus(RTOPEN)` → `save`.

## Wybrane rozwiązanie
**Opcja C: Multi-step agent z granularnymi narzędziami** - osobne narzędzie na każdą akcję API (Help, Reconfigure, GetStatus, SetStatus, Save) + ReadFile do odczytu danych lokalnych. Adapter OpenAI z możliwością podpięcia LM Studio.

## Architektura

```
RailwayAgent/
├── appsettings.json              # Konfiguracja (model, endpoint, klucze)
├── Program.cs                     # Entry point + DI
├── Config/
│   └── AgentConfig.cs            # POCO dla appsettings
├── Adapters/
│   └── OpenAiClientFactory.cs    # Factory: OpenAI / LM Studio / Azure
├── Tools/
│   ├── RailwayApiTools.cs        # 5 narzędzi: Help, Reconfigure, GetStatus, SetStatus, Save
│   └── FileTools.cs              # ReadFile tool
└── Services/
    └── RailwayApiClient.cs       # HttpClient wrapper z retry 503 + rate limit
```

## Plan implementacji

### Faza 1: Przygotowanie projektu
1. **`dotnet new console -n RailwayAgent`** w katalogu `05_task_agent/`
2. Dodanie NuGet packages:
   - `Microsoft.Agents.AI.OpenAI --prerelease`
   - `Azure.AI.OpenAI --prerelease`
   - `Microsoft.Extensions.Configuration.Json`
   - `Microsoft.Extensions.Http`
3. Utworzenie `appsettings.json`:
   ```json
   {
     "Agent": {
       "Provider": "openai",
       "Model": "gpt-5.2",
       "Endpoint": "https://api.openai.com/v1",
       "ApiKey": ""
     },
     "Railway": {
       "ApiUrl": "https://hub.ag3nts.org/verify",
       "ApiKey": "098c468b-70f4-4623-9d7a-fffd78149198",
       "TaskName": "railway",
       "MaxRetries": 5,
       "RetryDelayMs": 2000
     }
   }
   ```
   - `Agent.ApiKey` nadpisywany zmienną środowiskową `OPENAI_API_KEY`
   - `Agent.Provider`: `"openai"` (API) lub `"lmstudio"` (lokalny endpoint)

### Faza 2: Implementacja

#### 2.1 Config/AgentConfig.cs
- POCO klasy `AgentConfig` i `RailwayConfig` mapowane z `appsettings.json`
- `AgentConfig.ApiKey` z fallback na `Environment.GetEnvironmentVariable("OPENAI_API_KEY")`

#### 2.2 Adapters/OpenAiClientFactory.cs
- Factory pattern tworzący klienta OpenAI
- Dwa tryby:
  - **`openai`**: `new OpenAIClient(apiKey)` → `.GetResponsesClient(model)`
  - **`lmstudio`**: `new OpenAIClient(new ApiKeyCredential("lm-studio"), new OpenAIClientOptions { Endpoint = new Uri(config.Endpoint) })` → `.GetResponsesClient(model)`
- Metoda `.CreateAgent(instructions, tools)` zwracająca `AIAgent`

#### 2.3 Services/RailwayApiClient.cs
- `HttpClient` wrapper do `https://hub.ag3nts.org/verify`
- Metoda `SendAsync(object answer)`:
  - Buduje JSON body: `{ apikey, task: "railway", answer }`
  - Retry loop na HTTP 503 z exponential backoff
  - Odczyt nagłówków rate limit → `await Task.Delay()` jeśli limit bliski
  - Zwraca deserializowany JSON response jako `string`
- Logowanie każdego request/response do konsoli

#### 2.4 Tools/RailwayApiTools.cs
5 metod z atrybutem `[Description]`, każda wywołuje `RailwayApiClient`:

```csharp
[Description("Show available API actions and parameters")]
string Help()

[Description("Enable reconfigure mode for a given railway route")]
string Reconfigure([Description("Route code, e.g. x-01")] string route)

[Description("Get current status for a given railway route")]
string GetStatus([Description("Route code, e.g. x-01")] string route)

[Description("Set route status while in reconfigure mode. Allowed values: RTOPEN, RTCLOSE")]
string SetStatus([Description("Route code")] string route, [Description("Status value: RTOPEN or RTCLOSE")] string value)

[Description("Exit reconfigure mode and save changes for a given route")]
string Save([Description("Route code")] string route)
```

#### 2.5 Tools/FileTools.cs
```csharp
[Description("Read contents of a local file by path (CSV, JSON, TXT)")]
string ReadFile([Description("File path relative to working directory")] string path)
```

#### 2.6 Program.cs
```csharp
// 1. Load config (appsettings.json + env vars)
// 2. Create RailwayApiClient
// 3. Create tools instances
// 4. Create AIAgent via OpenAiClientFactory
// 5. agent.RunAsync(prompt) z instrukcją:
//    "Jesteś agentem zarządzającym trasami kolejowymi.
//     Przeczytaj plik trasy_wylaczone.csv aby poznać trasy.
//     Aktywuj trasę X-01 zmieniając jej status na RTOPEN.
//     Workflow: reconfigure → setstatus → save.
//     Obsługuj błędy - jeśli API zwróci błąd, przeczytaj go i spróbuj ponownie."
// 6. Wypisz wynik (szukaj flagi {FLG:...})
```

### Faza 3: Testowanie
1. Uruchomienie z `dotnet run`
2. Weryfikacja czy agent:
   - Wywołuje `Help` na start (opcjonalnie)
   - Wykonuje sekwencję `Reconfigure(x-01)` → `SetStatus(x-01, RTOPEN)` → `Save(x-01)`
   - Obsługuje 503 (retry)
   - Respektuje rate limity
   - Zwraca flagę `{FLG:...}`

## Parametry rozwiązania
- **Model**: gpt-5.2 (konfigurowalny)
- **Provider**: OpenAI direct / LM Studio (konfigurowalny via `appsettings.json`)
- **Retry**: max 5 prób, exponential backoff od 2s
- **Rate limit**: odczyt z nagłówków HTTP, automatyczny delay

## Oczekiwane rezultaty
- Działający agent C# który autonomicznie aktywuje trasę X-01
- Flaga `{FLG:...}` w odpowiedzi API
- Możliwość łatwego przełączenia na LM Studio (zmiana `Provider` + `Endpoint` w config)

## Struktura plików do utworzenia
1. `RailwayAgent/RailwayAgent.csproj`
2. `RailwayAgent/appsettings.json`
3. `RailwayAgent/Program.cs`
4. `RailwayAgent/Config/AgentConfig.cs`
5. `RailwayAgent/Adapters/OpenAiClientFactory.cs`
6. `RailwayAgent/Services/RailwayApiClient.cs`
7. `RailwayAgent/Tools/RailwayApiTools.cs`
8. `RailwayAgent/Tools/FileTools.cs`
