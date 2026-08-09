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


## 0b. Exact App Service settings diff (audited 2026-08-05)

Everything below was checked against the live values you exported.

### Broken — production cannot work until these change

| Setting | Why | Action |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | Pooler host resolves, but the project is deleted: `FATAL: (ENOTFOUND) tenant/user postgres.qyidgptrxjzweuczxyvq not found` | Point at a new Postgres |
| `EmailSettings__SmtpPassword` | Ends `…X1qhO7hZlgTPVh9Y` — the revoked key. Returns `535 Authentication failed` | Paste the reissued key (ends `…sEVGnmEif0VPmJk8`) |

### Rotate — valid, but exposed in a chat transcript

| Setting | Note |
|---|---|
| `JwtSettings__SecretKey` | Also human-authored and guessable in shape. A fresh value is in `.azure-jwt-secret.txt` (gitignored) |
| `AzureStorage__ConnectionString` | The account key works — a profile upload against it succeeded during testing. Rotate key1 in the portal and repaste |
| `GoogleAuthSettings__ClientSecret` | Reset it in Google Cloud Console -> Credentials |

### Correct — leave alone

`ASPNETCORE_ENVIRONMENT=Production`, `AzureStorage__ContainerName`,
`EmailSettings__FromEmail`, `__FromName`, `__SmtpHost`, `__SmtpPort`,
`__SmtpUsername`, `__SmtpUseSsl`, `GoogleAuthSettings__ClientId`,
`JwtSettings__Issuer`, `__Audience`, `__AccessTokenExpiryMinutes`,
`__RefreshTokenExpiryDays`.

### Correctly absent — do NOT add

`Seed__DemoData` and `Swagger__Enabled` are missing, which is exactly right:
`GetValue<bool>` returns false for an absent key, so demo accounts and Swagger
are both off. Adding them set to `true` would create sign-in-able seed accounts
in production.

### Worth adding

| Setting | Why |
|---|---|
| `EmailSettings__ApiKey` | Currently empty. A Brevo **API key** here sidesteps the SMTP IP allowlist entirely — it is plain HTTPS, so no `525 Unauthorized IP`. The code prefers this path when set. This is the least-effort fix for OTP. |

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

## 4b. Azure Blob CORS (needed for profile-banner colours)

The profile banner samples its colours from the user's picture, in the browser.
Reading pixels from an image requires the host to send CORS headers; Azure Blob
Storage sends none by default, so today every uploaded avatar falls back to the
generated gradient. Confirmed in the browser console:

    Access to image at 'https://miqatblob.blob.core.windows.net/user-images/...' blocked

Portal -> Storage account (miqatblob) -> **Resource sharing (CORS)** -> Blob service,
add one rule:

| field | value |
|---|---|
| Allowed origins | your frontend origin(s), e.g. `https://miqatsmartcalendar.vercel.app` |
| Allowed methods | GET, HEAD |
| Allowed headers | `*` |
| Exposed headers | `*` |
| Max age | 3600 |

Nothing breaks without this — the avatar still displays, the banner just uses
its fallback. Google-hosted avatars (Google sign-in) already work, because that
CDN does send the header.

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
