# Plan Wdrożenia: People Agent

## Data: 2026-03-15

## Problem do rozwiązania
Zadanie "people" z kursu AI Devs 4. Należy pobrać listę osób z Hub API, przefiltrować wg kryteriów demograficznych, otagować zawody modelem LLM (Structured Output), wybrać osoby z tagiem "transport" i wysłać wynik do weryfikacji.

## Wybrane rozwiązanie
**Opcja C: Hybrid Pipeline z Agent Framework do tagowania** — deterministyczna orkiestracja (CSV, filtrowanie, submission) z Microsoft Agent Framework użytym wyłącznie do klasyfikacji zawodów przez LLM.

## Plan implementacji

### Faza 1: Przygotowanie projektu

#### Krok 1.1: Utworzenie projektu .NET
- `dotnet new console -n PeopleAgent --framework net10.0`
- Dodanie zależności NuGet:
  - `Microsoft.Agents.AI.OpenAI` (1.0.0-rc4)
  - `Microsoft.Extensions.Configuration.Json`
  - `Microsoft.Extensions.Configuration.Binder`
  - `Microsoft.Extensions.Configuration.EnvironmentVariables`
  - `Spectre.Console` (0.54.0)

#### Krok 1.2: Reuse plików z 04_task_agent/SpkAgent
Skopiować i dostosować:
- `Config/AgentConfig.cs` — bez zmian (AgentConfig + VisionConfig)
- `Adapters/OpenAiClientFactory.cs` — bez zmian (CreateAgent + CreateChatClient)
- `Services/HubApiClient.cs` — zmienić typ `answer` z `{ declaration }` na tablicę obiektów Person
- `UI/ConsoleUI.cs` — bez zmian

#### Krok 1.3: appsettings.json
```json
{
  "Agent": {
    "Provider": "lmstudio",
    "Model": "qwen3-coder-30b-a3b-instruct-mlx",
    "Endpoint": "http://localhost:1234/v1"
  },
  "Hub": {
    "ApiUrl": "https://hub.ag3nts.org/verify",
    "ApiKey": "<klucz>",
    "TaskName": "people",
    "MaxRetries": 5,
    "RetryDelayMs": 2000
  }
}
```

### Faza 2: Implementacja

#### Krok 2.1: Model danych — `Models/Person.cs`
```csharp
public class Person
{
    public string Name { get; set; }
    public string Surname { get; set; }
    public string Gender { get; set; }  // "M" / "F"
    public int Born { get; set; }       // rok urodzenia
    public string City { get; set; }
    public string Job { get; set; }     // opis stanowiska (z CSV)
    public List<string> Tags { get; set; } = new();
}
```

#### Krok 2.2: Serwis CSV — `Services/CsvService.cs`
- `DownloadCsvAsync(string url)` — pobranie pliku CSV przez HttpClient
- `ParseCsv(string csvContent)` — parsowanie do `List<Person>`
  - Obsługa nagłówków CSV (rozpoznanie kolumn po nazwie)
  - Separator: `,` lub `;` (sprawdzić w pobranym pliku)

#### Krok 2.3: Narzędzie tagowania — `Tools/TaggingTools.cs`
- Klasa z atrybutem `[Description]` dla Agent Framework
- Metoda `TagJobs(string numberedJobsList)` → zwraca JSON z tagami
- Agent instructions z opisami tagów:
  - IT — praca z komputerami, programowanie, administracja systemów
  - transport — kierowcy, logistyka, spedycja, przewozy
  - edukacja — nauczyciele, szkoleniowcy, wykładowcy
  - medycyna — lekarze, pielęgniarki, farmaceuci
  - praca z ludźmi — obsługa klienta, HR, sprzedaż
  - praca z pojazdami — mechanicy, serwisanci, operatorzy maszyn
  - praca fizyczna — budownictwo, magazyn, produkcja
- Structured Output: `response_format` z JSON Schema wymuszającym tablicę `{id, tags[]}`

#### Krok 2.4: Orkiestracja — `Program.cs`
Pipeline:
1. Załaduj konfigurację z appsettings.json
2. Pobierz CSV → `CsvService.DownloadCsvAsync()`
3. Parsuj → `CsvService.ParseCsv()`
4. Filtruj: `Gender == "M"`, `City == "Grudziądz"`, `Born >= 1986 && Born <= 2006`
5. Przygotuj ponumerowaną listę jobów
6. Utwórz AIAgent z `TaggingTools` i instructions
7. `agent.InvokeAsync()` — agent taguje joby
8. Przypisz tagi do osób
9. Filtruj: tylko osoby z tagiem "transport"
10. Zbuduj payload JSON
11. Wyślij przez `HubApiClient`
12. Wyświetl wynik (szukaj `{FLG:...}`)

### Faza 3: Testowanie

#### Krok 3.1: Weryfikacja lokalna
- `dotnet build` — kompilacja bez błędów
- Sprawdzenie czy LM Studio działa na localhost:1234

#### Krok 3.2: Test end-to-end
- `dotnet run`
- Weryfikacja: CSV pobrane, filtrowanie poprawne, tagi przypisane, odpowiedź z Hub

#### Krok 3.3: Debug w razie błędu
- Sprawdzenie logów Spectre.Console
- Weryfikacja formatu JSON odpowiedzi (born=int, tags=string[])
- Retry z innymi opisami tagów jeśli LLM źle klasyfikuje

## Parametry rozwiązania
- LLM: qwen3-coder-30b-a3b-instruct-mlx (lokalny via LM Studio)
- Batch: wszystkie joby w jednym wywołaniu LLM
- Structured Output: JSON Schema w response_format
- Rate limiting: 5 retries, 2000ms base delay

## Oczekiwane rezultaty
- Flaga `{FLG:...}` z Hub API po poprawnym wysłaniu danych
- Otagowane osoby z transportu spełniające kryteria demograficzne
