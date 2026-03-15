# Dokumentacja Wdrożenia: People Agent

## Data: 2026-03-15

## Środowisko
- .NET 10.0
- Microsoft.Agents.AI.OpenAI 1.0.0-rc4
- Spectre.Console 0.54.0
- LM Studio: qwen3-coder-30b-a3b-instruct-mlx @ localhost:1234

## Konfiguracja wykonana

### Krok 1: Utworzenie projektu
```bash
dotnet new console -n PeopleAgent --framework net10.0
```
**Status**: OK

### Krok 2: Struktura katalogów
```
PeopleAgent/
├── Config/AgentConfig.cs       (reuse z SpkAgent)
├── Adapters/OpenAiClientFactory.cs (reuse z SpkAgent + CreateOpenAiClient)
├── Models/Person.cs            (nowy)
├── Services/CsvService.cs      (nowy - CSV parser z obsługą quoted fields)
├── Services/HubApiClient.cs    (reuse z SpkAgent - zmieniony na SubmitPeopleAsync)
├── Tools/TaggingTools.cs       (nowy - AIAgent tool + instrukcje tagowania)
├── UI/ConsoleUI.cs             (reuse z SpkAgent + PrintStep)
├── Program.cs                  (nowy - 6-krokowy pipeline)
└── appsettings.json
```
**Status**: OK

### Krok 3: Napotkane problemy i rozwiązania

1. **CSV format**: Kolumny to `birthDate` (pełna data YYYY-MM-DD) i `birthPlace` (nie `born`/`city`). Naprawiono mapowanie w CsvService.
2. **Quoted CSV fields**: Pole `job` zawiera przecinki w cudzysłowach. Dodano parser obsługujący quoted fields.
3. **AIAgent tool calling**: Lokalny model qwen3-coder nie obsługuje poprawnie tool calling przez Agent Framework. Agent zwracał pustą odpowiedź bez wywołania narzędzia.
4. **ChatResponseFormat.Json**: LM Studio zwraca 400 Bad Request przy użyciu `response_format`. Obejście: prompt engineering z instrukcją zwrócenia JSON.
5. **Rozwiązanie**: Dwustopniowe podejście — najpierw próba AIAgent z tools, fallback na ChatClient z promptem JSON.

**Status**: OK — wszystkie problemy rozwiązane

### Krok 4: Test end-to-end
```bash
dotnet run
```
- CSV pobrane: 24417 osób
- Po filtrowaniu (M, Grudziądz, 1986-2006): 31 osób
- Otagowane przez LLM: 31 osób
- Z tagiem "transport": 12 osób
- Submission: **{FLG:SURVIVORS}**

**Status**: OK

## Testy i weryfikacja
- Build: 0 warnings, 0 errors
- Run: flaga zdobyta za pierwszym razem
- Tagowanie: LLM poprawnie sklasyfikował zawody z opisów w języku polskim

## Instrukcje utrzymania
- Klucz API: `appsettings.json` → `Hub.ApiKey`
- LM Studio musi być uruchomione na `localhost:1234` przed `dotnet run`
