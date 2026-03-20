#!/bin/bash
# Register ProxyAgent endpoint in Hub
# Usage: ./register.sh <ngrok-url>
# Example: ./register.sh https://unimminent-vasiliki-urethral.ngrok-free.dev

URL="${1:?Usage: ./register.sh <ngrok-url>}"
API_KEY="<HUB_API_KEY>"

curl -X POST Hub__ApiUrl \
  -H "Content-Type: application/json" \
  -d "{
    \"apikey\": \"${API_KEY}\",
    \"task\": \"proxy\",
    \"answer\": {
      \"url\": \"${URL}\",
      \"sessionID\": \"session-001\"
    }
  }"

echo ""
