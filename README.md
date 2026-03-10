# Ecomail Proxy

Azure Function proxy pro přihlašování k newsletteru přes Ecomail API. Řeší CORS a skrývá API klíč před klientským kódem.

## Endpoint

```
POST https://ecomailproxy.azurewebsites.net/api/newsletter-subscribe
Content-Type: application/json

{ "email": "user@example.com" }
```

### Odpovědi

| Status | Body | Popis |
|--------|------|-------|
| 200 | `{"ok":true}` | Email úspěšně přihlášen |
| 400 | `{"ok":false,"error":"..."}` | Neplatný email nebo request |
| 403 | `{"ok":false,"error":"Forbidden."}` | Chybí nebo neplatný Origin header |
| 429 | `{"ok":false,"error":"Too many requests..."}` | Rate limit (max 5 req/min per IP) |
| 500 | `{"ok":false,"error":"..."}` | Chyba serveru nebo Ecomail API |

## Zabezpečení

- **Origin check** — povoluje pouze requesty s `Origin: https://redflags.cz`
- **CORS** — `Access-Control-Allow-Origin` omezen na `https://redflags.cz`
- **Rate limiting** — max 5 requestů za 60s per IP
- **Body size limit** — max 1 KB

## Lokální vývoj

1. Nastavit `local.settings.json`:
   ```json
   {
     "IsEncrypted": false,
     "Values": {
       "AzureWebJobsStorage": "UseDevelopmentStorage=true",
       "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
       "ECOMAIL_API_KEY": "<your-api-key>",
       "ECOMAIL_LIST_ID": "28"
     }
   }
   ```

2. Spustit:
   ```bash
   func start
   ```

3. Otestovat:
   ```bash
   curl -X POST http://localhost:7071/api/newsletter-subscribe \
     -H "Content-Type: application/json" \
     -H "Origin: https://redflags.cz" \
     -d '{"email":"test@example.com"}'
   ```

## Deploy

1. Vytvořit Function App v Azure Portal (runtime .NET 10 Isolated, Consumption plan)
2. Nastavit v **Settings → Environment variables**:
   - `ECOMAIL_API_KEY`
   - `ECOMAIL_LIST_ID`
3. Deploy přes `func azure functionapp publish <name>` nebo Rider

## Technologie

- C# / .NET 10, Azure Functions v4 (isolated worker model)
