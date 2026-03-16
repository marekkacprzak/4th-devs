# Dokumentacja Wdrożenia: OpenTelemetry Tracing z Aspire Dashboard

## Data: 2026-03-16

## Środowisko
- .NET 10.0, CategorizeAgent project
- Microsoft.Agents.AI.OpenAI v1.0.0-rc4
- OpenTelemetry + OTLP exporter

## Konfiguracja wykonana

### Krok 1.1: NuGet packages
Dodano do `CategorizeAgent.csproj`:
- `OpenTelemetry` v1.*
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` v1.*

**Status**: ✅

### Krok 1.2: appsettings.json
Dodano sekcję `Telemetry`:
```json
"Telemetry": {
  "Enabled": true,
  "OtlpEndpoint": "http://localhost:4317",
  "ServiceName": "CategorizeAgent",
  "EnableSensitiveData": true
}
```
**Status**: ✅

### Krok 2.1: Config/TelemetryConfig.cs
Utworzono model konfiguracji z polami: Enabled, OtlpEndpoint, ServiceName, EnableSensitiveData.

**Status**: ✅

### Krok 2.2: Telemetry/TelemetrySetup.cs
Utworzono klasę IDisposable konfigurującą:
- TracerProvider z OTLP exporter (sources: ServiceName, Microsoft.Extensions.AI, Microsoft.Agents.AI)
- MeterProvider z OTLP exporter (meters: Microsoft.Extensions.AI, Microsoft.Agents.AI)
- Graceful no-op when Enabled=false

**Status**: ✅

### Krok 2.3: Adapters/OpenAiClientFactory.cs
Dodano opcjonalny parametr `TelemetryConfig?`. Gdy telemetria jest włączona, wstawia `.UseOpenTelemetry()` w pipeline IChatClient przed `.AsAIAgent()`.

**Status**: ✅

### Krok 2.4: Program.cs
- Załadowanie TelemetryConfig z konfiguracji
- `using var telemetry = new TelemetrySetup(telemetryConfig)` — auto-dispose
- Przekazanie config do CreateAgent

**Status**: ✅

## Testy i weryfikacja

### Build
```
dotnet build → Build succeeded. 0 Warning(s) 0 Error(s)
```
**Status**: ✅

## Uruchomienie Aspire Dashboard
```bash
docker run --rm -d -p 18888:18888 -p 4317:18889 \
  --name aspire-dashboard \
  mcr.microsoft.com/dotnet/aspire-dashboard:latest
```
Dashboard UI: http://localhost:18888

## Wyłączenie telemetrii
W `appsettings.json` ustaw `"Enabled": false` — zero overhead, brak zmian w logice biznesowej.
