# Plan Wdrożenia: OpenTelemetry Tracing z Aspire Dashboard

## Data: 2026-03-16

## Problem do rozwiązania
Projekt CategorizeAgent nie posiada żadnej telemetrii — jedynym outputem jest `ConsoleUI` (Spectre.Console). Brak visibility w:
- Czas trwania wywołań LLM
- Zużycie tokenów (prompt/completion/cached)
- Czas wykonania tool calls
- Pełny trace flow agenta (agent → chat → tool → chat)

## Wybrane rozwiązanie
**Aspire Dashboard z OpenTelemetry OTLP exporter** — web UI do eksploracji traces/metrics/logs bez konfiguracji chmurowej. Jeden `docker run` i gotowe.

## Plan implementacji

### Faza 1: Przygotowanie (NuGet + Docker)

**Krok 1.1** — Dodaj pakiety NuGet do `CategorizeAgent.csproj`:
```xml
<PackageReference Include="OpenTelemetry" Version="1.*" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.*" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.*" />
<PackageReference Include="Microsoft.Extensions.Logging.Console" Version="10.*" />
```

**Krok 1.2** — Dodaj konfigurację OTLP w `appsettings.json`:
```json
"Telemetry": {
  "Enabled": true,
  "OtlpEndpoint": "http://localhost:4317",
  "ServiceName": "CategorizeAgent",
  "EnableSensitiveData": true
}
```

### Faza 2: Implementacja

**Krok 2.1** — Utwórz `Config/TelemetryConfig.cs` — model konfiguracji telemetrii.

**Krok 2.2** — Utwórz `Telemetry/TelemetrySetup.cs` — statyczna klasa konfigurująca:
- `TracerProvider` z `AddOtlpExporter()` i source `"CategorizeAgent"` + `"*Microsoft.Extensions.AI"`
- `MeterProvider` z `AddOtlpExporter()` i meter `"*Microsoft.Extensions.AI"`
- `LoggerFactory` z `AddOpenTelemetry()` + OTLP exporter
- Resource builder z service name

**Krok 2.3** — Zmodyfikuj `Adapters/OpenAiClientFactory.cs`:
- W `CreateAgent()`: dodaj `.AsBuilder().UseOpenTelemetry(...)` w pipeline IChatClient przed `.AsAIAgent()`
- Przekaż `TelemetryConfig` jako parametr

**Krok 2.4** — Zmodyfikuj `Program.cs`:
- Załaduj `TelemetryConfig` z konfiguracji
- Wywołaj `TelemetrySetup.Initialize()` na starcie
- Dispose providers na koniec (using)
- Przekaż config do factory

### Faza 3: Testowanie

**Krok 3.1** — Uruchom Aspire Dashboard:
```bash
docker run --rm -d -p 18888:18888 -p 4317:18889 \
  --name aspire-dashboard \
  mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

**Krok 3.2** — `dotnet build` — weryfikacja kompilacji

**Krok 3.3** — `dotnet run` — uruchom agenta i sprawdź:
- Traces widoczne w http://localhost:18888
- Spans: `invoke_agent CategorizeAgent` → `chat qwen3-...` → `execute_tool RunClassificationCycle`
- Metrics: token usage, operation duration
- Logs: function invocations

## Parametry rozwiązania
- **Exporter**: OTLP → Aspire Dashboard
- **Traces sources**: `CategorizeAgent`, `*Microsoft.Extensions.AI`
- **Metrics meters**: `*Microsoft.Extensions.AI`
- **Sensitive data**: enabled (prompts/completions visible in traces)
- **Konfigurowalny**: on/off via `appsettings.json`

## Oczekiwane rezultaty
- Pełna widoczność w agent flow w Aspire Dashboard UI
- Zero impact na istniejącą logikę biznesową
- Łatwe on/off przez config
- Token usage tracking per request

## Pliki do modyfikacji/utworzenia
| Plik | Akcja |
|------|-------|
| `CategorizeAgent.csproj` | Modify — dodaj NuGet packages |
| `appsettings.json` | Modify — dodaj sekcję Telemetry |
| `Config/TelemetryConfig.cs` | Create — model konfiguracji |
| `Telemetry/TelemetrySetup.cs` | Create — setup OpenTelemetry |
| `Adapters/OpenAiClientFactory.cs` | Modify — UseOpenTelemetry w pipeline |
| `Program.cs` | Modify — init/dispose telemetrii |
