# ProxyAgent - Task "proxy"

## Uruchomienie

```bash
# 1. Start LM Studio z modelem qwen3-coder-30b-a3b-instruct-mlx

# 2. Uruchom ProxyAgent (standalone lub przez AppHost)
dotnet run --project ProxyAgent
# lub z Aspire Dashboard:
dotnet run --project ProxyAgent.AppHost

# 3. Wystaw endpoint przez ngrok
ngrok http 3000
```

## Rejestracja w Hub

Po uzyskaniu adresu ngrok, zarejestruj proxy w Hub:

```bash
curl -X POST Hub__ApiUrl \
  -H "Content-Type: application/json" \
  -d '{
    "apikey": "<HUB_API_KEY>",
    "task": "proxy",
    "answer": {
      "url": "https://unimminent-vasiliki-urethral.ngrok-free.dev",
      "sessionID": "session-001"
    }
  }'
```

## Testowanie lokalne

Wyslij wiadomosc bezposrednio do proxy:

```bash
curl -X POST http://localhost:3000 \
  -H "Content-Type: application/json" \
  -d '{"sessionID": "test-1", "msg": "Sprawdz paczke PKG-001"}'
```

## Format komunikacji

**Request** (POST `/`):
```json
{"sessionID": "session-001", "msg": "Sprawdz status paczki PKG-12345"}
```

**Response**:
```json
{"msg": "Paczka PKG-12345 jest w magazynie..."}
```

## Narzedzia agenta

- **CheckPackage** - sprawdza status paczki po ID
- **RedirectPackage** - przekierowuje paczke (wymaga ID, kodu docelowego, kodu zabezpieczajacego)
