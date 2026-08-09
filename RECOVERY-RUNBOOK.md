# Miqat Production Recovery Runbook

Audited 2026-08-06 against the live App Service, the live database host, and
this repository's full history.

Shell variables used throughout — set these once per session. No secret values
appear in this document; every one is read from the environment or a file.

```bash
export RG=Miqat
export APP=MiqatSmartCalendar
export STORAGE=miqatblob
export VERCEL=https://mqiatsmartcalendar.vercel.app
export API=https://miqatsmartcalendar-b7e4anhxh8d5cmcx.israelcentral-01.azurewebsites.net
az account set --subscription e239fa8f-505d-4d26-b708-fba2abd0caa9
```

---

## 0. Read this first — production is running old code

```bash
curl -s -o /dev/null -w "swagger:  %{http_code}\n" $API/swagger/index.html
curl -s -o /dev/null -w "hub:      %{http_code}\n" -X POST "$API/hubs/miqat/negotiate?negotiateVersion=1"
```

Observed right now: **swagger 200, hub 404**.

Both prove the deployed build predates the local commits. Swagger is gated to
development in the current code, and the SignalR hub only exists in it. So
until section 5 is done:

- `/swagger` is publicly exposing the full API surface
- SignalR verification (§2.4) **cannot pass** — there is no hub to negotiate with
- The `Boards` and `LinkedCommentId` migrations are not in the deployed assembly

Unpushed: **7 commits in Miqat.Api, 7 in Miqat** (14 total).

Push before attempting §2.4, §2.5 or anything that depends on `Boards`.

---

## 1. Version alignment

### 1.1 Verify

```bash
# App Service runtime stack
az webapp config show -g $RG -n $APP \
  --query "{linuxFx:linuxFxVersion, netFramework:netFrameworkVersion, alwaysOn:alwaysOn}" -o table

# What the platform actually loaded (authoritative; the setting above can lie
# if the image is self-contained)
az webapp log tail -g $RG -n $APP 2>/dev/null | head -40
```

Passing: `linuxFxVersion` is `DOTNETCORE|8.0` (or a container image built
`FROM mcr.microsoft.com/dotnet/aspnet:8.0`).

```bash
# Local SDK and the global EF tool
dotnet --list-sdks
dotnet tool list --global | grep dotnet-ef

# Every EF/Npgsql package reference in the solution
grep -rhoP 'Include="(Microsoft\.EntityFrameworkCore[.\w]*|Npgsql[.\w]*)" Version="[^"]+"' \
  --include=*.csproj . | sort -u

# Target framework
grep -rhoP '<TargetFramework>[^<]+' --include=*.csproj . | sort -u
```

Audited values in this repo — all consistent, nothing to change:

| Component | Version |
|---|---|
| TargetFramework | `net8.0` |
| Microsoft.EntityFrameworkCore(.Design/.Relational/.Tools) | `8.0.23` |
| Npgsql.EntityFrameworkCore.PostgreSQL | `8.0.8` |

### 1.2 What a mismatch looks like at runtime

| Symptom | Cause |
|---|---|
| `The framework 'Microsoft.NETCore.App', version '9.0.0' was not found` on startup | Project retargeted past the App Service stack |
| `dotnet ef` fails with `Could not load ... EntityFrameworkCore, Version=9.0` | Global `dotnet-ef` newer than the project's EF packages |
| Migration applies locally, no-ops in Azure | Deployed assembly predates the migration — this is the current §0 state |
| `Npgsql...InvalidCastException` on `jsonb` columns | Npgsql major mismatched against EF Core major |
| App boots, every query throws `RelationalTypeMapping` | EF Core and EF Relational on different patch trains |

### 1.3 Pin

```bash
# Pin the EF tool to the packages' train
dotnet tool uninstall --global dotnet-ef
dotnet tool install --global dotnet-ef --version 8.0.23

# Pin the SDK for this repo so a machine with .NET 9 does not silently roll forward
cat > global.json <<'JSON'
{
  "sdk": {
    "version": "8.0.0",
    "rollForward": "latestFeature"
  }
}
JSON

az webapp config set -g $RG -n $APP --linux-fx-version "DOTNETCORE|8.0"
```

---

## 2. Verification checklist

### 2.1 Postgres connectivity *from the App Service*

Your machine reaching the database proves nothing about App Service egress.
Run it from inside the container:

```bash
# SSH into the running App Service container
az webapp ssh -g $RG -n $APP
```

Inside that shell:

```bash
# Resolve + TCP reach, without needing psql installed
getent hosts aws-0-eu-west-1.pooler.supabase.com
timeout 10 bash -c 'cat < /dev/null > /dev/tcp/aws-0-eu-west-1.pooler.supabase.com/5432' \
  && echo "TCP OK" || echo "TCP BLOCKED"
```

Passing: an IP from `getent`, then `TCP OK`.

The end-to-end proof is the app itself — any authenticated 200 means EF opened
a pooled connection:

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST $API/api/Auth/login \
  -H "Content-Type: application/json" -d '{"email":"probe@example.com","password":"wrong"}'
```

Passing: **400** or **401**. A **500** means the connection failed.

> Use a password that **passes** validation, or the 400 is meaningless — a short
> password is rejected before the DB is touched, which reads as a false pass.
> Observed 2026-08-06: short password 400, valid-shape payload **500** in
> exactly 5.0s. The configured Supabase tenant does not exist, so every
> DB-touching request fails.

### 2.2 Migration history

```bash
# From the App Service log stream, at startup
az webapp log tail -g $RG -n $APP | grep -m1 "\[Migration\]"
```

Passing: `[Migration] ✅ Database migrated successfully.`

Against the database directly — `$PGHOST`, `$PGUSER`, `$PGPASSWORD` from your
password manager, never inline:

```bash
psql "host=$PGHOST port=5432 dbname=postgres user=$PGUSER sslmode=require" \
  -c 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";'
```

Passing — exactly these five, in this order:

```
20260411130135_InitialPostgres
20260501203934_AddFriendsAndMentionsSystem
20260805094156_AddComments
20260805170521_AddNotificationLinkedComment
20260805XXXXXX_AddBoards
```

Confirm none pending:

```bash
dotnet ef migrations list --project Miqat.infrastructure.persistence \
  --startup-project Miqat.Persistence
```

Passing: no entry printed with `(Pending)`.

> The last two will be absent until §5 — they ship with the unpushed commits.

### 2.3 OTP email over the Brevo **API** path

The code prefers the API when `EmailSettings__ApiKey` is non-empty and only
falls back to SMTP otherwise. The two paths log different lines, which is how
you confirm which one ran.

```bash
# Terminal 1
az webapp log tail -g $RG -n $APP | grep -iE "via Brevo|via SMTP|\[Email\]|\[Dev OTP\]"

# Terminal 2
curl -s -w "\n%{http_code}\n" -X POST $API/api/Auth/forgot-password \
  -H "Content-Type: application/json" -d '{"email":"YOUR_REAL_ADDRESS"}'
```

| Log line | Meaning |
|---|---|
| `Email sent to ... via Brevo on attempt 1` | **Passing** — API path delivered |
| `Email sent to ... via SMTP (smtp-relay.brevo.com)` | API key missing or blank; it fell through |
| `[Email] Failed: ... "code":"unauthorized" ... unrecognised IP address` | The sender IP is not on Brevo's allowlist — §4.0. Applies to the API path too |
| `[Email] Failed: ...` + `[Dev OTP] ...` | Delivery failed; the code is logging the OTP instead |

HTTP **200** with the Brevo line, and the mail in your inbox, is the pass.
HTTP **502** means delivery genuinely failed — the endpoint no longer lies
about this.

Cross-check on Brevo's side: **Transactional → Logs** should show the message.

### 2.4 SignalR over WebSockets

```bash
# Requires §5 (the hub does not exist in the deployed build — currently 404)
TOKEN=$(curl -s -X POST $API/api/Auth/login -H "Content-Type: application/json" \
  -d '{"email":"YOUR_EMAIL","password":"YOUR_PASSWORD"}' | jq -r .accessToken)

curl -s -X POST "$API/hubs/miqat/negotiate?negotiateVersion=1" \
  -H "Authorization: Bearer $TOKEN" | jq '.availableTransports[].transport'
```

Passing: the list **includes `"WebSockets"`**. If it shows only
`ServerSentEvents` and `LongPolling`, web sockets are off at the platform
(§3).

Prove the socket actually opens:

```bash
npm i -g wscat
wscat -c "wss://${API#https://}/hubs/miqat?access_token=$TOKEN"
```

Passing: `Connected (press CTRL+C to quit)`.

### 2.5 Blob upload from the Vercel origin, CORS satisfied

Preflight exactly as a browser would:

```bash
curl -s -i -X OPTIONS \
  "https://$STORAGE.blob.core.windows.net/user-images/probe.png" \
  -H "Origin: $VERCEL" \
  -H "Access-Control-Request-Method: GET" | grep -i "^HTTP/\|access-control-allow"
```

Passing: `HTTP/1.1 200` **and** an `Access-Control-Allow-Origin` header echoing
`$VERCEL`. No such header = the rule in §3 is not applied, and profile-banner
colour sampling will silently fall back.

Upload path end to end:

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST $API/api/User/upload-profile-image \
  -H "Authorization: Bearer $TOKEN" -F "file=@/path/to/test.png"
```

Passing: **200** with `{ profileImageUrl }`. A **503** means
`AzureStorage__ConnectionString` is unset or wrong.

### 2.6 Google OAuth round trip

Not scriptable end to end — the consent screen is interactive. Verify the
configuration, then do one manual pass.

```bash
# The client id must be public-facing and match the frontend build
az webapp config appsettings list -g $RG -n $APP \
  --query "[?name=='GoogleAuthSettings__ClientId'].value" -o tsv
grep -n "googleClientId" ../../Front-End/Miqat/src/environments/environment.ts
```

Passing: the two strings are identical.

In Google Cloud Console → Credentials → your OAuth client, **Authorised
JavaScript origins** must contain `$VERCEL` and `http://localhost:4200`.

Manual pass: sign in with Google on the deployed site, then confirm the account
exists and is flagged as a Google account:

```bash
psql "$PGCONN" -c \
  'SELECT "Email","IsGoogleAccount","IsVerified" FROM "Users" ORDER BY "CreatedAt" DESC LIMIT 3;'
```

Passing: your address, `IsGoogleAccount = t`, `IsVerified = t`.

### 2.7 JWT issue → refresh → revoke

```bash
LOGIN=$(curl -s -X POST $API/api/Auth/login -H "Content-Type: application/json" \
  -d '{"email":"YOUR_EMAIL","password":"YOUR_PASSWORD"}')
ACCESS=$(echo "$LOGIN"  | jq -r .accessToken)
REFRESH=$(echo "$LOGIN" | jq -r .refreshToken)

# 1. Issue — the access token works
curl -s -o /dev/null -w "authenticated call: %{http_code}\n" \
  $API/api/User/me -H "Authorization: Bearer $ACCESS"

# 2. Refresh — a NEW pair comes back
NEW=$(curl -s -X POST $API/api/Auth/refresh-token -H "Content-Type: application/json" \
  -d "{\"refreshToken\":\"$REFRESH\"}")
echo "$NEW" | jq -r '.accessToken != null'
NEW_REFRESH=$(echo "$NEW" | jq -r .refreshToken)
[ "$NEW_REFRESH" != "$REFRESH" ] && echo "rotated: yes" || echo "rotated: NO — token reuse"

# 3. Revoke — the OLD refresh token must now be rejected
curl -s -o /dev/null -w "old refresh replayed: %{http_code}\n" \
  -X POST $API/api/Auth/refresh-token -H "Content-Type: application/json" \
  -d "{\"refreshToken\":\"$REFRESH\"}"
```

Passing: `200`, `true`, `rotated: yes`, and the replay returns **401**. A 200
on the replay means old refresh tokens stay valid — a real security finding.

---

## 3. Portal configuration, verified from the CLI

### 3.1 HTTPS Only

```bash
az webapp show -g $RG -n $APP --query "httpsOnly" -o tsv          # verify
az webapp update -g $RG -n $APP --set httpsOnly=true              # set
```

Passing: `true`.

### 3.2 Web sockets

```bash
az webapp config show -g $RG -n $APP --query "webSocketsEnabled" -o tsv   # verify
az webapp config set  -g $RG -n $APP --web-sockets-enabled true           # set
```

Passing: `true`. Also confirm `alwaysOn`:

```bash
az webapp config show -g $RG -n $APP --query "alwaysOn" -o tsv
```

`false` on a Free/Shared tier means the app unloads when idle and drops every
SignalR connection.

### 3.3 Blob CORS

> **Destructive:** `cors clear` removes *all* existing blob CORS rules for the
> account. List them first and re-add anything you still need.

```bash
az storage cors list --services b --account-name $STORAGE --auth-mode login -o table
```

```bash
az storage cors clear --services b --account-name $STORAGE --auth-mode login

az storage cors add --services b --account-name $STORAGE --auth-mode login \
  --methods GET HEAD OPTIONS \
  --origins "$VERCEL" "http://localhost:4200" "http://localhost:4208" \
  --allowed-headers "*" --exposed-headers "*" --max-age 3600
```

Verify — do not trust the portal blade:

```bash
az storage cors list --services b --account-name $STORAGE --auth-mode login -o json
```

Then the browser-truth check in §2.5. `GET`/`HEAD` are what an `<img>` and a
canvas read need; `OPTIONS` covers the preflight.

---

## 4. Secret rotation

Order matters: least disruptive first, and the two that log users out are last
so you are not debugging auth while something else is mid-rotation.

### 4.0 Brevo authorised IPs — do this before anything else email-related

**Tested 2026-08-06 and this corrects an earlier claim in this file: the IP
allowlist applies to the transactional _API_ as well as SMTP.** Sending with a
valid API key from an unlisted address returns:

```
Error calling SendTransacEmail: {"message":"We have detected you are using an
unrecognised IP address <ip>...","code":"unauthorized"}
```

So switching from SMTP to the API key does not avoid it. Fix the allowlist
itself: <https://app.brevo.com/security/authorised_ips>

- **Recommended:** disable the IP restriction. App Service outbound IPs change
  on scale and plan changes, so an allowlist silently breaks email later.
- **If you keep it:** add every App Service outbound IP —

```bash
az webapp show -g $RG -n $APP --query "outboundIpAddresses" -o tsv | tr ',' '\n'
az webapp show -g $RG -n $APP --query "possibleOutboundIpAddresses" -o tsv | tr ',' '\n'
```

Add `possibleOutboundIpAddresses` too — that is the full set the platform may
use, not just the current ones.

**Verify:** §2.3 must log `via Brevo`, not `[Email] Failed`.

### 4.1 Brevo API key — replacing the exposed one

**Blast radius:** OTP email only. Nothing else touches it. Users already signed
in are unaffected; anyone mid-registration must press *Resend code*.

1. Brevo → **SMTP & API → API Keys** → create a new key, then **delete the old
   one** (the value pasted in chat).
2. Apply — the value is read from your environment, never typed into a command:

```bash
read -rs BREVO_API_KEY && export BREVO_API_KEY
az webapp config appsettings set -g $RG -n $APP \
  --settings EmailSettings__ApiKey="$BREVO_API_KEY"
unset BREVO_API_KEY
```

**Restart:** automatic — App Service recycles on an app-settings change.

**Verify:** §2.3. The log line must say `via Brevo`.

### 4.2 `JwtSettings__SecretKey`

**Blast radius: every signed-in user is logged out immediately.** Access tokens
fail signature validation, and refresh tokens are rejected because the refresh
endpoint validates the expired access token's signature first. Do this in a
quiet window.

The current value is also human-authored and guessable in shape — rotate it on
those grounds alone, not only because it was exposed.

```bash
# A fresh value already exists, gitignored, generated during the hardening pass
az webapp config appsettings set -g $RG -n $APP \
  --settings JwtSettings__SecretKey="$(cat .azure-jwt-secret.txt)"
```

**Restart:** automatic.

**Verify:** a *fresh* login must succeed and a token minted before the change
must fail.

```bash
curl -s -o /dev/null -w "old token: %{http_code}  (expect 401)\n" \
  $API/api/User/me -H "Authorization: Bearer $ACCESS"
```

Then clear browser storage and sign in again.

### 4.3 `AzureStorage__ConnectionString` — key1, then key2

Storage accounts carry two keys precisely so this is zero-downtime. **Do not
rotate both at once.**

```bash
# 1. Which key is in use? Compare its tail against the app setting.
az storage account keys list -g $RG -n $STORAGE --query "[].keyName" -o tsv

# 2. Switch the app to key2 FIRST, while key1 is still valid
az webapp config appsettings set -g $RG -n $APP --settings \
  AzureStorage__ConnectionString="$(az storage account show-connection-string \
    -g $RG -n $STORAGE --key secondary --query connectionString -o tsv)"
```

Verify uploads still work (§2.5), **then**:

```bash
# 3. Now key1 is unused — rotating it breaks nothing
az storage account keys renew -g $RG -n $STORAGE --key primary
```

Repeat in reverse days later to retire key2.

**Blast radius during the window:** none, if the order above is followed.
Renewing the key the app is currently using causes immediate 403s on every
avatar upload and read.

### 4.4 `GoogleAuthSettings__ClientSecret`

**Blast radius:** new Google sign-ins fail between reset and redeploy. Existing
sessions are unaffected — the secret is only used at the token exchange.

Google Cloud Console → **APIs & Services → Credentials** → your OAuth 2.0
client → **Add secret**, then apply and delete the old one:

```bash
read -rs GOOGLE_CLIENT_SECRET && export GOOGLE_CLIENT_SECRET
az webapp config appsettings set -g $RG -n $APP \
  --settings GoogleAuthSettings__ClientSecret="$GOOGLE_CLIENT_SECRET"
unset GOOGLE_CLIENT_SECRET
```

Google supports two live secrets during rotation — add the new one, deploy,
verify a real sign-in (§2.6), then delete the old.

**The client _id_ is public** and does not need rotating.

### 4.5 `EmailSettings__SmtpPassword` — the fallback

**Blast radius:** none while `EmailSettings__ApiKey` is set; SMTP is only the
fallback. Set it correctly anyway so the fallback is not a dead end.

```bash
read -rs BREVO_SMTP_KEY && export BREVO_SMTP_KEY
az webapp config appsettings set -g $RG -n $APP \
  --settings EmailSettings__SmtpPassword="$BREVO_SMTP_KEY"
unset BREVO_SMTP_KEY
```

**Verify** the fallback in isolation — temporarily blank the API key, send one
OTP, confirm `via SMTP` in the log, then restore the API key.

### 4.6 Confirm every value landed

```bash
# Names and lengths only — never the values
az webapp config appsettings list -g $RG -n $APP \
  --query "[].{name:name, len:length(value)}" -o table
```

---

## 5. Push, then purge the history

### 5.1 Push first

Purging rewrites history. Do it **after** the pending work is in, or you will
rewrite a base your local branches no longer share.

```bash
cd ~/dev/Back-End/Miqat.Api && git push origin main
cd ~/dev/Front-End/Miqat   && git push origin main
```

Re-run §0 — swagger must now be **404** and the hub **200**.

### 5.2 What is actually exposed

Audited across all refs of Miqat.Api:

| Secret | Commits | Files |
|---|---|---|
| Database password | 4 | `Miqat.Persistence/appsettings.json`, `Miqat.Persistence/bin/Debug/net8.0/appsettings.json` |
| JWT signing key | 4 | same |
| Brevo SMTP key | 2 | same |
| Azure Storage account key | 2 | same |

**`HEAD` is already clean** — 0 matches. The Brevo *API* key and the Google
client secret were **never committed**; they were only exposed in chat, so
§4.1 and §4.4 fully close them.

Repo scale: 56 commits, 3 branches, **0 tags**.

### 5.3 Purge

> **Irreversible and rewrites every commit hash.** Every collaborator must
> re-clone or hard-reset. Take a backup first — the mirror below *is* your
> backup; keep it until you are satisfied.

```bash
pip install --user git-filter-repo

cd ~/dev/Back-End
git clone --mirror https://github.com/Eslam-mohamed2/Miqat.Api.git Miqat.Api-purge
cp -r Miqat.Api-purge Miqat.Api-BACKUP-$(date +%Y%m%d)     # keep until verified
cd Miqat.Api-purge
```

Build the replacement file. Each line is `literal==>replacement`. Populate it
from your password manager — this file must never be committed:

```bash
cat > /tmp/miqat-replacements.txt <<EOF
${OLD_DB_PASSWORD}==>REMOVED-DB-PASSWORD
${OLD_JWT_KEY}==>REMOVED-JWT-KEY
${OLD_SMTP_KEY}==>REMOVED-SMTP-KEY
${OLD_STORAGE_KEY}==>REMOVED-STORAGE-KEY
EOF
chmod 600 /tmp/miqat-replacements.txt
```

```bash
git filter-repo --replace-text /tmp/miqat-replacements.txt

# The build output should never have been tracked; drop it from history too
git filter-repo --path-glob '*/bin/*' --path-glob '*/obj/*' --invert-paths --force
```

Verify **before** pushing:

```bash
for pat in "$OLD_DB_PASSWORD" "$OLD_JWT_KEY" "$OLD_SMTP_KEY" "$OLD_STORAGE_KEY"; do
  echo -n "match count: "; git log --all --oneline -S"$pat" | wc -l
done
# every line must print 0

git grep -I "REMOVED-DB-PASSWORD" $(git rev-list --all) | head   # replacements present
```

```bash
shred -u /tmp/miqat-replacements.txt
```

### 5.4 Force-push

> **Destructive:** overwrites every branch and tag on the remote.

```bash
git remote add origin https://github.com/Eslam-mohamed2/Miqat.Api.git
git push --force --mirror origin
```

### 5.5 GitHub's cached unreferenced commits

Force-pushing does **not** remove commits GitHub still has cached — old SHAs
stay reachable by direct URL, and forks keep their own copies.

1. Open a GitHub Support ticket asking them to **garbage-collect unreferenced
   objects and purge the cache** for `Eslam-mohamed2/Miqat.Api`. This is the
   only way; there is no self-service button.
2. Check for forks — `https://github.com/Eslam-mohamed2/Miqat.Api/network/members`.
   A fork is an independent copy your rewrite cannot touch. If any exist, the
   secrets are permanently public and **rotation is the only real remedy**.
3. The repository is **public**. Assume everything in it has been scraped.
   Treat §4 as mandatory, not precautionary — the purge is hygiene, the
   rotation is the fix.

### 5.6 Collaborators

Anyone with a clone must, after the force-push:

```bash
# Simplest and safest
cd .. && rm -rf Miqat.Api && git clone https://github.com/Eslam-mohamed2/Miqat.Api.git

# Or, keeping local work — rebase it onto the rewritten history
git fetch origin
git rebase --onto origin/main $(git merge-base HEAD origin/main) HEAD
```

**Never** `git pull` into an old clone — it reintroduces the purged commits and
undoes the rewrite on the next push.

---

## 6. Preventing recurrence

### 6.1 `.gitignore` for this stack

Already applied in this repo; confirm nothing regressed:

```bash
git check-ignore -v .env .azure-jwt-secret.txt Miqat.Persistence/bin
git ls-files | grep -cE "/(bin|obj)/"        # must be 0
git ls-files | grep -iE "\.env$|secret|\.pfx$|\.pem$"   # only .env.example
```

Ensure these lines exist:

```gitignore
**/bin/
**/obj/
.env
.env.*
!.env.example
*.pfx
*.pem
appsettings.*.local.json
.azure-jwt-secret.txt
```

### 6.2 Azure Key Vault with managed identity

```bash
export KV=miqat-kv

az keyvault create -g $RG -n $KV --location israelcentral \
  --enable-rbac-authorization true --enable-purge-protection true

# System-assigned identity for the app
az webapp identity assign -g $RG -n $APP
PRINCIPAL=$(az webapp identity show -g $RG -n $APP --query principalId -o tsv)

# Least privilege: read secrets, nothing else
az role assignment create --assignee "$PRINCIPAL" \
  --role "Key Vault Secrets User" \
  --scope "$(az keyvault show -n $KV --query id -o tsv)"

# Your own account needs write access to seed the vault
az role assignment create --assignee "$(az ad signed-in-user show --query id -o tsv)" \
  --role "Key Vault Secrets Officer" \
  --scope "$(az keyvault show -n $KV --query id -o tsv)"
```

Key Vault names cannot contain `:`, so the provider maps `--` onto it —
`ConnectionStrings--DefaultConnection` becomes `ConnectionStrings:DefaultConnection`:

```bash
az keyvault secret set --vault-name $KV --name "ConnectionStrings--DefaultConnection" --value "$PGCONN"
az keyvault secret set --vault-name $KV --name "JwtSettings--SecretKey"               --value "$(cat .azure-jwt-secret.txt)"
az keyvault secret set --vault-name $KV --name "EmailSettings--ApiKey"                --value "$BREVO_API_KEY"
az keyvault secret set --vault-name $KV --name "AzureStorage--ConnectionString"       --value "$STORAGE_CONN"
az keyvault secret set --vault-name $KV --name "GoogleAuthSettings--ClientSecret"     --value "$GOOGLE_CLIENT_SECRET"
```

Packages:

```bash
dotnet add Miqat.Persistence/Miqat.API.csproj package Azure.Identity
dotnet add Miqat.Persistence/Miqat.API.csproj package Azure.Extensions.AspNetCore.Configuration.Secrets
```

`Program.cs` — immediately after `var builder = WebApplication.CreateBuilder(args);`
and **before** any `builder.Configuration` read:

```csharp
// Key Vault is layered over App Settings, so it wins where both define a key.
// Guarded on the URI being present: local development has no vault and must
// keep working from user-secrets, which is what the else-branch relies on.
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUri),
        new DefaultAzureCredential());
}
```

Then the only secret-ish setting left in App Settings is the vault's address,
which is not a secret:

```bash
az webapp config appsettings set -g $RG -n $APP \
  --settings KeyVault__Uri="https://$KV.vault.azure.net/"

# Remove the migrated ones
az webapp config appsettings delete -g $RG -n $APP --setting-names \
  ConnectionStrings__DefaultConnection JwtSettings__SecretKey \
  EmailSettings__ApiKey EmailSettings__SmtpPassword \
  AzureStorage__ConnectionString GoogleAuthSettings__ClientSecret
```

Verify the app reads from the vault:

```bash
az webapp log tail -g $RG -n $APP | grep -i "keyvault\|Azure.Identity"
curl -s -o /dev/null -w "%{http_code}\n" -X POST $API/api/Auth/login \
  -H "Content-Type: application/json" -d '{"email":"probe@example.com","password":"wrong"}'
```

Passing: **400**, not 500. A 500 with `AuthenticationFailedException` means the
role assignment has not propagated — it can take several minutes.

### 6.3 Pre-commit secret scanning

```bash
# Ubuntu/WSL
curl -sSfL https://github.com/gitleaks/gitleaks/releases/latest/download/gitleaks_8.18.4_linux_x64.tar.gz \
  | tar -xz -C /tmp gitleaks && sudo mv /tmp/gitleaks /usr/local/bin/

# Scan the current tree
gitleaks detect --source . --no-git --redact -v

# Scan all history — expect findings until §5.3 is done
gitleaks detect --source . --redact -v
```

Wire it into the repo:

```bash
pip install --user pre-commit
cat > .pre-commit-config.yaml <<'YAML'
repos:
  - repo: https://github.com/gitleaks/gitleaks
    rev: v8.18.4
    hooks:
      - id: gitleaks
YAML
pre-commit install
git add .pre-commit-config.yaml && git commit -m "Add gitleaks pre-commit hook"
```

Test it refuses a secret:

```bash
# Build the fake key at runtime so this document itself never contains a
# string matching the provider's format — GitHub push protection scans on
# shape, not value, and will block a commit containing one.
printf 'ApiKey="xkeysib-%s-%s"\n' "$(printf '0%.0s' {1..64})" "$(printf '0%.0s' {1..16})" > /tmp/canary.cs
cp /tmp/canary.cs ./canary.cs && git add canary.cs && git commit -m "canary"
# expect: the hook blocks the commit
git reset HEAD canary.cs && rm canary.cs
```

A hook is bypassable with `--no-verify`, so add server-side enforcement:
GitHub → **Settings → Code security → Secret scanning** and **Push protection**
(free for public repositories).

### 6.4 Local development without secrets in `appsettings.json`

`appsettings.json` already ships blank values. Use user-secrets, which live
outside the repository entirely:

```bash
cd Miqat.Persistence
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=miqat;Username=miqat;Password=..."
dotnet user-secrets set "JwtSettings:SecretKey" "$(openssl rand -base64 64 | tr -d '\n')"
dotnet user-secrets list
```

Stored at `~/.microsoft/usersecrets/<UserSecretsId>/secrets.json` — never in the
working tree, and only read when `ASPNETCORE_ENVIRONMENT=Development`.

For the Docker path this repo already uses, `.env` + `docker-compose.yml`
remains correct; `.env` is gitignored and `.env.example` documents the shape.

```bash
# Confirm no secret is required to be in the tree for a local run
git stash list && git status --short     # clean
docker compose up -d && sleep 20
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:10000/api/Auth/login \
  -H "Content-Type: application/json" -d '{"email":"x@y.z","password":"nope"}'   # 400
```

---

## Order of execution

1. **§5.1** push — production is running old code, and `/swagger` is public
2. **§3** portal toggles — cheap, and §2.4/§2.5 depend on them
3. **§2** verification sweep — establishes a known-good baseline
4. **§4.1** Brevo API key — closes the most recently exposed secret
5. **§4.3** storage key1→key2, **§4.4** Google, **§4.5** SMTP
6. **§4.2** JWT last — it logs everyone out
7. **§5.3–5.6** history purge, then **§5.5** GitHub support ticket
8. **§6** Key Vault, gitleaks, user-secrets
