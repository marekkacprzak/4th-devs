# CHANGELOG

## 2026-03-15 - Railway Agent
- Wdrożono agenta C# z MS Agent Framework (Microsoft.Agents.AI.OpenAI)
- 5 granularnych narzędzi API: Help, Reconfigure, GetStatus, SetStatus, Save
- Adapter OpenAI/LM Studio z konfiguracją via appsettings.json
- Retry na 503 + obsługa rate limitów z nagłówków HTTP
- Wynik: trasa X-01 aktywowana, flaga `{FLG:COUNTRYROADS}`
