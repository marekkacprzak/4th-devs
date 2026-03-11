# Logistics Assistant API

REST API do obsługi operatora systemu logistycznego z funkcją zarządzania paczkami.

## Instalacja

```bash
# Zainstaluj zależności
bun install
# lub
npm install
```

## Konfiguracja

Skopiuj plik `.env.example` na `.env` i uzupełnij wymagane klucze API:

```bash
cp .env.example .env
```

Edytuj `.env` i uzupełnij:
- `OPENAI_API_KEY` - Twój klucz API OpenAI
- `USER_API_KEY` - Klucz API do systemu zarządzania paczkami
- `PORT` (opcjonalne) - Port serwera (domyślnie 3000)

## Uruchomienie

```bash
# Przy użyciu Bun
bun run app.js

# Lub Node.js
node app.js

# Tryb development z automatycznym restartem
npm run dev
```

Serwer uruchomi się na `http://localhost:3000`

## API Endpoints

### POST /message

Wysyła wiadomość do asystenta logistycznego.

**Request:**
```json
{
  "sessionID": "dowolny-id-sesji",
  "msg": "Wiadomość do asystenta"
}
```

**Response:**
```json
{
  "msg": "Odpowiedź asystenta"
}
```

### GET /

Health check endpoint - sprawdza czy serwer działa.

**Response:**
```json
{
  "status": "ok",
  "message": "Logistics Assistant API is running"
}
```

## Przykładowe użycie

```bash
# Sprawdź status serwera
curl http://localhost:3000

# Wyślij wiadomość
curl -X POST http://localhost:3000/message \
  -H "Content-Type: application/json" \
  -d '{
    "sessionID": "test-session-1",
    "msg": "Witaj! Chciałbym sprawdzić status paczki PKG12345678"
  }'
```

## Funkcjonalność

Asystent ma dostęp do następujących narzędzi:

- **check_package** - Sprawdza status i lokalizację paczki
- **redirect_package** - Przekierowuje paczkę do nowej lokalizacji (wymaga kodu zabezpieczającego)

Każda sesja (identyfikowana przez `sessionID`) utrzymuje niezależną historię konwersacji, co pozwala na obsługę wielu operatorów jednocześnie.

## Architektura

```
03_task/
├── app.js              # Główny serwer Express
├── src/
│   ├── ai.js          # Integracja z OpenAI API
│   ├── prompts.js     # Prompt systemowy
│   ├── session.js     # Zarządzanie sesjami i historią rozmów
│   └── tools.js       # Narzędzia API pakietów
├── package.json
└── .env.example
```

## Misja

Cel: Namierzyć paczkę z częściami do reaktora, zdobyć kod zabezpieczający i przekierować przesyłkę do elektrowni w Żarnowcu (kod: `PWR6132PL`).

Jeśli misja zostanie wykonana prawidłowo, operator przekaże sekretny kod w formacie `{FLG:XXXX}`, który zostanie automatycznie wykryty i wyświetlony w logach serwera.
