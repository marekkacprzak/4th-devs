# Plan Wdrożenia: Dynamiczny rate limit w RailwayApiClient

## Data: 2026-03-15

## Problem do rozwiązania
API Railway zwraca 429 z nagłówkami rate limit (`retry-after`, `x-ratelimit-reset`, `x-ratelimit-policy: 1;w=30`), ale obecny kod:
1. Nie traktuje 429 jako auto-retry - zwraca błąd agentowi, który musi ponownie wywołać tool (koszt tokenów)
2. Nie zapamiętuje `x-ratelimit-reset` między wywołaniami
3. Nagłówki rate limit pojawiają się TYLKO przy 429, nie przy 200

## Wybrane rozwiązanie
**Opcja B: Dynamiczny delay** oparty na `x-ratelimit-reset` + `retry-after` z auto-retry na 429.

## Plan implementacji

### Faza 1: Modyfikacja RailwayApiClient.cs

Zmiany w jednym pliku: `Services/RailwayApiClient.cs`

#### 1.1 Dodanie pola `_nextAllowedCall`
```csharp
private DateTimeOffset _nextAllowedCall = DateTimeOffset.MinValue;
private const int FallbackDelayMs = 5000;
```
Przechowuje timestamp kiedy wolno wykonać kolejny call. Aktualizowane po każdym response.

#### 1.2 Nowa metoda `WaitForRateLimit()` - wywoływana PRZED każdym requestem
```csharp
private async Task WaitForRateLimit()
{
    var now = DateTimeOffset.UtcNow;
    if (_nextAllowedCall > now)
    {
        var waitMs = (int)(_nextAllowedCall - now).TotalMilliseconds;
        Console.WriteLine($"  [API] Rate limit: waiting {waitMs}ms until next allowed call...");
        await Task.Delay(waitMs);
    }
}
```

#### 1.3 Nowa metoda `UpdateRateLimitState()` - wywoływana PO każdym response
Parsuje nagłówki i body:
- `retry-after` (nagłówek) → ustawia `_nextAllowedCall`
- `x-ratelimit-reset` (nagłówek, unix timestamp) → ustawia `_nextAllowedCall`
- `retry_after` z body JSON (fallback) → ustawia `_nextAllowedCall`
- Jeśli 200 i brak nagłówków → `_nextAllowedCall = now + FallbackDelayMs`

#### 1.4 Auto-retry na 429
429 traktowany jak 503 - automatyczny retry po odczekaniu `retry-after`, NIE zwracany do agenta. Dodany do pętli retry.

#### 1.5 Usunięcie starej metody `RespectRateLimit()`
Zastąpiona przez `WaitForRateLimit()` + `UpdateRateLimitState()`.

### Faza 2: Testowanie
1. `dotnet build`
2. `dotnet run` - weryfikacja że agent nie dostaje 429 (auto-retry)
3. Logi powinny pokazywać "waiting Xms until next allowed call"

## Parametry rozwiązania
- **Fallback delay**: 5s (gdy brak nagłówków rate limit)
- **Auto-retry**: 429 + 503 w pętli retry
- **Max retries**: bez zmian (5)

## Oczekiwane rezultaty
- Agent nie widzi 429 - mniej tool-calls = mniej tokenów
- Dynamiczne czekanie na podstawie nagłówków API
- Fallback 5s gdy brak info
