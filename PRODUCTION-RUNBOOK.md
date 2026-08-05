# Production go-live runbook

State as of 2026-08-05: all code work is done, committed locally, and verified.
The steps below are the ones that need **your** accounts — in order.

## 0. Push (needs your GitHub login, once)

```bash
# either install gh and login, or add an SSH key / PAT
cd ~/dev/Back-End/Miqat.Api   && git push origin main   # triggers Azure deploy workflow
cd ~/dev/Front-End/Miqat      && git push origin main   # triggers Vercel deploy
```

The backend workflow runs the 13 unit tests before publishing.

## 1. Database (hard blocker — the old Supabase project is deleted)

Create a Postgres 16 instance (Supabase free / Neon free / Azure Flexible Server).
Then in **Azure Portal → MiqatSmartCalendar → Configuration**, set:

```
ConnectionStrings__DefaultConnection = Host=<host>;Port=5432;Database=<db>;Username=<user>;Password=<pw>;Ssl Mode=Require
```

Migrations run automatically on startup. Nothing else to do.

## 2. Secrets to rotate (all four appeared in a chat transcript)

| Secret | Where to rotate | Where to paste |
|---|---|---|
| JWT signing key | none — freshly generated | `JwtSettings__SecretKey` in App Service. Value is in `.azure-jwt-secret.txt` in this repo folder (gitignored — do NOT commit it) |
| Azure Storage key | Portal → Storage account → Access keys → Rotate key1 | `AzureStorage__ConnectionString` in App Service |
| Google client secret | console.cloud.google.com → Credentials → your OAuth client → reset secret | `GoogleAuthSettings__ClientSecret` in App Service |
| Brevo SMTP key | already re-issued; **also fix Authorised IPs** (below) | `EmailSettings__SmtpPassword` in App Service |

The local `.env` already carries a new local-only JWT secret (rotated + verified).

## 3. Brevo — email still blocked by IP restriction

The new key authenticates but Brevo rejects the sender IP:
`525 5.7.1 Unauthorized IP address`.

Brevo → **Settings → Security → Authorised IPs**: either disable the
restriction (recommended — your home IP changes), or add:
- your current IP (dev): `41.41.250.187`
- Azure outbound IPs: Portal → App Service → Properties → *Outbound IP addresses* (add all)

Then verify: request a password reset for your real address and confirm the
email arrives. Until this is done, nobody can register or reset a password
in production (they now get an honest 502, not a fake success).

## 4. App Service toggles (Portal, two clicks)

- **Configuration → General settings → HTTPS Only: On**
- **Configuration → General settings → Web sockets: On** (SignalR falls back to
  long-polling silently without it)
- Confirm `Seed__DemoData` is **absent or false** (defaults to off; the startup
  log must show: `Demo data disabled (Seed:DemoData is not true)`)
- Confirm `ASPNETCORE_ENVIRONMENT` is **not** `Development`

## 5. First-boot verification checklist

1. App Service log stream shows `[Migration] ✅` and `[Seeder] ⏭️ Demo data disabled`
2. `/swagger` returns 404
3. Register a fresh account through the UI → OTP email arrives → verify → login
4. Sign in with Google (never machine-tested — do this manually)
5. Upload a profile photo (exercises Azure Blob with the rotated key)
6. Open two browsers, comment on a shared task → appears live in the other
   (proves WebSockets is on)

## Verified locally before handover

- prod-mode boot against empty DB: 0 users seeded, swagger 404, 0 SQL logged
- full UI registration → OTP → verify → login → dashboard: 7/7
- empty-account sweep of all 10 pages: clean, no console errors
- suites: 13/13 backend · 39/39 main · 8/8 boards · 8/8 cards
