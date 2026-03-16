# Dokumentacja Wdrożenia: Dynamiczny rate limit

## Data: 2026-03-15

## Środowisko
- .NET 10.0.201
- Testowano z: lmstudio/qwen3-coder-30b + openai/gpt-5.2

## Konfiguracja wykonana

### Krok 1: Modyfikacja RailwayApiClient.cs
Dodano:
- Pole `_nextAllowedCall` (DateTimeOffset) - timestamp następnego dozwolonego calla
- Stała `FallbackDelayMs = 5000` - fallback gdy brak nagłówków
- `WaitForRateLimit()` - czeka PRZED requestem do `_nextAllowedCall`
- `UpdateRateLimitState()` - PO response parsuje nagłówki i body:
  - `x-ratelimit-reset` (unix timestamp)
  - `retry-after` (sekundy, nagłówek)
  - `retry_after` (sekundy, body JSON)
  - fallback 5s przy 200 bez nagłówków
- Auto-retry na 429 (nie zwraca błędu agentowi)
- `DelayBeforeRetry()` uwzględnia rate limit delay (max z backoff vs rate limit)

Usunięto:
- Stara metoda `RespectRateLimit()`

**Status**: ✅

### Krok 2: Build
```
dotnet build → 0 warnings, 0 errors
```
**Status**: ✅

### Krok 3: Test run
- 429 auto-retry: ✅ (2x w przebiegu, agent nie widział błędu)
- Dynamiczny delay: ✅ (25499ms, 19499ms z retry-after)
- Fallback 5s: ✅ (po 200 bez nagłówków)
- 503 retry: ✅ (3x w przebiegu)
- Flaga: ✅ `{FLG:COUNTRYROADS}`
**Status**: ✅
