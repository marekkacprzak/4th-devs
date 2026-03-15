# Plan Wdrożenia: Console UI z Spectre.Console

## Data: 2026-03-15

## Problem do rozwiązania
Obecny output agenta używa prostego `Console.ForegroundColor` z formatem `[Tool] Name(...)` i `[API] ...`. Brakuje wizualnej hierarchii, separatorów i estetyki. Celem jest dodanie kolorowych paneli, ASCII bannerów i czytelnych ramek do tool calls i workflow.

## Wybrane rozwiązanie
**Spectre.Console** — biblioteka NuGet oferująca FigletText, Panel, Rule, Markup, Table. Minimalna ilość kodu, profesjonalny efekt.

## Plan implementacji

### Faza 1: Przygotowanie
1. Dodać NuGet package `Spectre.Console` do `RailwayAgent.csproj`
2. Utworzyć klasę `UI/ConsoleUI.cs` jako centralny punkt formatowania

### Faza 2: Implementacja

#### 2.1 Klasa `ConsoleUI` (`UI/ConsoleUI.cs`)
Statyczna klasa z metodami:
- `PrintBanner(string title)` — FigletText na start/koniec agenta
- `PrintToolCall(string name, string? params)` — Panel z ikoną wrench dla tool calls
- `PrintApiRequest(int attempt, int max, string json)` — szary markup dla requestów
- `PrintApiResponse(int statusCode, string body)` — zielony/czerwony w zależności od kodu
- `PrintRateLimit(int waitMs)` — żółty markup z ikoną zegara
- `PrintRetry(string reason)` — żółty markup
- `PrintError(string message)` — czerwony panel z ikoną
- `PrintResult(string result)` — zielony panel na końcowy wynik
- `PrintInfo(string message)` — szary/dimmed tekst informacyjny

#### 2.2 Aktualizacja `Program.cs`
- Zamienić `Console.ForegroundColor/WriteLine/ResetColor` na `ConsoleUI.PrintBanner()` i `ConsoleUI.PrintResult()`

#### 2.3 Aktualizacja `Tools/RailwayApiTools.cs`
- Zamienić 5x blok cyan `Console.ForegroundColor` na `ConsoleUI.PrintToolCall()`

#### 2.4 Aktualizacja `Tools/FileTools.cs`
- Zamienić blok cyan na `ConsoleUI.PrintToolCall()`

#### 2.5 Aktualizacja `Services/RailwayApiClient.cs`
- Zamienić ~8 bloków `Console.ForegroundColor` na odpowiednie metody `ConsoleUI`:
  - Request → `PrintApiRequest()`
  - Response → `PrintApiResponse()`
  - Rate limit wait → `PrintRateLimit()`
  - 503/429 retry → `PrintRetry()`
  - Network error → `PrintError()`
  - Next call info → `PrintInfo()`
  - Retry delay → `PrintInfo()`

### Faza 3: Testowanie
1. `dotnet build` — kompilacja bez błędów
2. Uruchomienie agenta i weryfikacja wizualna outputu
3. Sprawdzenie scenariuszy: sukces, retry 429, retry 503, błąd sieci

## Parametry rozwiązania
- **Biblioteka:** `Spectre.Console` (latest stable)
- **Nowe pliki:** 1 (`UI/ConsoleUI.cs`)
- **Zmodyfikowane pliki:** 4 (`Program.cs`, `RailwayApiTools.cs`, `FileTools.cs`, `RailwayApiClient.cs`) + `RailwayAgent.csproj`
- **Usunięte pliki:** 0

## Oczekiwane rezultaty
- ASCII banner "RAILWAY AGENT" na starcie
- Panele z ramkami dla tool calls
- Kolorowe statusy API (zielony=ok, żółty=retry, czerwony=error)
- Rule separatory między krokami
- Czytelna hierarchia: tool → request → response
