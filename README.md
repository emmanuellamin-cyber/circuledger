# CircuLedger API

Backend for the **Smart Youth Mobile Circularity Hub**, a youth-led pilot by a student technology team at
Njala University (UNDP × Samsung Youth Mobile Circularity Innovation Challenge, Sierra Leone).

The Hub buys faulty phones from citizens, wipes and **certifies** their data destruction, dismantles devices,
recovers components and hazardous materials, and resells refurbished handsets and parts. A fixed share of
every sale is **reinvested** into the device-purchasing Seed Fund and youth training. Every significant step
is written to an **append-only, hash-chained ledger**.

```
Awareness → Hybrid intake (USSD/SMS · drop-off · batched pickup) → Sanitization & certificate
          → Resource accounting (dismantle · components · hazardous kg) → Marketplace → Reinvestment
```

Pilot targets tracked on the dashboard: 500 devices, ≥50 kg hazardous material diverted, 20 technicians
trained, 5 jobs, 5 schools + 2 campuses reached, ≥40% young women. Currency: **SLE**.

---

## Solution layout

```
CircuLedger.sln
├── src/
│   ├── CircuLedger.Domain/          Entities, enums, state transitions, pure domain logic
│   │   ├── Ledger/                  JsonCanonicalizer, LedgerHasher, LedgerChainVerifier
│   │   ├── Geo/                     Haversine
│   │   └── Services/                DispatchBatcher, ReinvestmentCalculator
│   ├── CircuLedger.Application/     Use cases (services), DTOs, FluentValidation validators, ports
│   ├── CircuLedger.Infrastructure/  EF Core + Npgsql, Identity/JWT, ledger writer, RSA signer,
│   │                                QuestPDF/QRCoder, AES-GCM/HMAC protector, file storage, seeding,
│   │                                migrations
│   └── CircuLedger.Api/             Controllers, versioning, ProblemDetails, rate limiting, CORS,
│                                    OpenAPI + Swagger UI, Serilog, health checks
├── tests/CircuLedger.Tests/         xUnit unit tests + Testcontainers integration tests
├── Dockerfile · docker-compose.yml · .env.example
└── CircuLedger.http                 End-to-end request collection
```

Dependencies point inwards: `Api → Infrastructure → Application → Domain`. Application talks to the database
through `IApplicationDbContext`, and to the outside world through ports such as `ISmsSender`, `IFileStorage`,
`ICertificateSigner`, `IGeocoder` and `IUserDirectory`.

---

## Quick start with Docker

Requires Docker Desktop / Docker Engine with Compose v2.

```bash
cp .env.example .env          # then edit: set passwords and generate fresh keys (see below)
docker compose up -d --build
```

| Service  | URL                                   | Notes                                       |
|----------|---------------------------------------|---------------------------------------------|
| API      | http://localhost:8080/swagger         | Swagger UI ("Authorize" accepts the JWT)    |
| Health   | http://localhost:8080/health          | includes the `postgresql` check             |
| pgAdmin  | http://localhost:5050                 | add server: host `db`, port `5432`          |
| Postgres | localhost:5432                        | data in named volume `circuledger_pgdata`   |

On startup the API applies EF migrations and seeds the roles, the admin user, the 4 drop-off points, the pricing
matrix, the 30% reinvestment rule, the funds (25,000 SLE initial seed grant) and, if `SEED_DEMO_DATA=true`, demo
staff and sample devices. Uploads and the certificate signing key are kept in the `circuledger_appdata` volume.

Generate secrets:

```bash
openssl rand -base64 32   # PHONE_HASH_KEY, IMEI_HASH_KEY, PHONE_ENCRYPTION_KEY (each separately)
openssl rand -base64 48   # JWT_SIGNING_KEY
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out cert-signing.pem   # optional; else auto-generated
```

Useful commands:

```bash
docker compose logs -f api
docker compose down            # keeps volumes (data survives)
docker compose down -v         # DESTROYS database, uploads and signing key
```

> ⚠️ **Back up the certificate signing key** (`/app/App_Data/keys/certificate-signing.pem` in the
> `circuledger_appdata` volume, or your `CERT_SIGNING_PRIVATE_KEY_PEM`). If you lose it, previously issued
> certificates can no longer be verified. To rotate, move the old public key into
> `Certificates:Signing:RetiredPublicKeysPem`.

### Demo accounts (when demo data is enabled)

| Email                        | Role           | Password (`.env.example` / Development) |
|------------------------------|----------------|-----------------------------------------|
| admin@circuledger.local      | Admin          | `ChangeMe!Admin2026`                    |
| ops@circuledger.local        | OperationsLead | `ChangeMe!Demo2026`                     |
| tech@circuledger.local       | Technician (safety-trained) | `ChangeMe!Demo2026`        |
| agent@circuledger.local      | FieldAgent     | `ChangeMe!Demo2026`                     |
| buyer@circuledger.local      | Buyer          | `ChangeMe!Demo2026`                     |

---

## Deploying to Render

`render.yaml` is a Render Blueprint that sets up a Docker web service, a managed Postgres database and a 1 GB disk for
`App_Data`. In the Render dashboard, choose **New → Blueprint**, pick this repo, then enter the values marked
`sync: false` (public URL, CORS origins). The JWT and data-protection keys and the admin password are in `appsettings.json`.

- The container listens on Render's `$PORT`. It falls back to 8080 when `PORT` isn't set.
- `ConnectionStrings__CircuLedgerDb` accepts Render's `postgres://…` URL as well as Npgsql key/value strings.
- Migrations and seeding run on startup. `/health` is the health check path.

---

## Local development (without Docker for the API)

Prerequisites: **.NET 10 SDK** and PostgreSQL 16 (local install, or just the compose DB:
`docker compose up -d db`).

```bash
# Create the role/database once if you use a local PostgreSQL install:
psql -U postgres -c "CREATE ROLE circuledger LOGIN PASSWORD 'circuledger_dev';"
psql -U postgres -c "CREATE DATABASE circuledger OWNER circuledger;"

dotnet tool restore                      # installs dotnet-ef (pinned in dotnet-tools.json)
dotnet run --project src/CircuLedger.Api # http://localhost:5097/swagger
```

`appsettings.Development.json` contains **throwaway development keys** and passwords so the app runs out of the
box. Never reuse them in production. Override any setting with environment variables using `__` for nesting,
e.g. `ConnectionStrings__CircuLedgerDb`, `Jwt__SigningKey`, `DataProtection__PhoneHashKey`.

Connection string format (key `ConnectionStrings:CircuLedgerDb`):

```
Host=localhost;Port=5432;Database=circuledger;Username=circuledger;Password=...
```

### EF Core migrations

```bash
# add a migration
dotnet ef migrations add <Name> \
  --project src/CircuLedger.Infrastructure --startup-project src/CircuLedger.Api \
  --output-dir Persistence/Migrations

# apply to the database in ConnectionStrings:CircuLedgerDb (the API also does this on startup)
dotnet ef database update --project src/CircuLedger.Infrastructure --startup-project src/CircuLedger.Api

# produce an idempotent SQL script for a DBA-managed deployment
dotnet ef migrations script --idempotent -o migrate.sql \
  --project src/CircuLedger.Infrastructure --startup-project src/CircuLedger.Api
```

Included migrations:

* `InitialCreate` creates all tables (snake_case), indexes (tracking ID, certificate number, IMEI hash, phone
  hash, ...), PostgreSQL sequences, a `jsonb` ledger payload, and a non-negative fund balance check.
* `LedgerAppendOnlyProtection` runs `REVOKE UPDATE, DELETE, TRUNCATE ON ledger_entries` and adds a trigger that
  rejects UPDATE/DELETE/TRUNCATE for every role. The trigger is needed because table owners and superusers
  bypass privilege checks. Set `Seed__MigrateOnStartup=false` if migrations are run by a separate deploy step.

**Production hardening:** run migrations as an owner role, and run the API as a separate role with only
`SELECT, INSERT` on `ledger_entries` (plus normal DML on other tables).

### Tests

```bash
dotnet test                                                     # all tests (needs Docker for integration)
dotnet test --filter "FullyQualifiedName~Unit"                  # unit only, no Docker
dotnet test --filter "FullyQualifiedName~Integration"           # Testcontainers.PostgreSql (postgres:16-alpine)
```

| Area | Tests |
|------|-------|
| Ledger | hash chaining, tamper detection (payload, metadata, deletion, re-hashing), jsonb reformatting, µs timestamps |
| Reinvestment | 20/10 split, cent rounding, invalid rules |
| Dispatch | Haversine distance, radius grouping, max batch size, oldest-first, transport suggestion |
| Certificates | RSA sign/verify, tampering, wrong key, key rotation, third-party verification with public key only |
| Seed fund | purchase blocked when the fund is insufficient, balances, credits |
| USSD | full menu flows (drop-off and pickup), cancel, invalid input, lookups, determinism, screen length |
| Integration | full pipeline USSD → … → KPIs; 409 on low seed fund; safety-training enforcement; raw-SQL ledger tampering blocked; concurrent ledger writes; USSD idempotency and privacy; SMS keywords; refresh-token reuse detection; role policies; validation ProblemDetails; health check |

---

## Testing the USSD flow

The callback follows the Africa's Talking format: `POST` form fields `sessionId`, `serviceCode`, `phoneNumber`,
`text`. `text` is the **cumulative** input joined by `*`. The response is `text/plain` and starts with
`CON ` (continue) or `END ` (final).

```bash
URL=http://localhost:8080/api/v1/ussd/callback
S=ATUid_test_1; P=+23276123456
u() { curl -s -X POST $URL -d "sessionId=$S" -d "serviceCode=*384*1234#" -d "phoneNumber=$P" --data-urlencode "text=$1"; echo; echo ---; }

u ""                          # CON main menu
u "1"                         # brands
u "1*1"                       # Samsung → condition
u "1*1*2"                     # broken screen → drop-off or pickup
u "1*1*2*1"                   # drop-off points list
u "1*1*2*1*1"                 # confirm screen with estimated offer
u "1*1*2*1*1*1"               # END Thank you! Ref: SYXXXXXX
u "1*1*2*1*1*1"               # retry → identical answer, no duplicate request
S=ATUid_test_2; u "2*SYXXXXXX"   # status (only for the phone that created it)
S=ATUid_test_3; u "3*CDS-2026-000001"   # verify a certificate
S=ATUid_test_4; u "4*Towama"     # nearest drop-off points (km via Haversine)
```

Menu: `1` Sell a phone → brand → condition (dead / broken screen / working-faulty) → drop-off point **or**
pickup (area/landmark) → confirm → reference code · `2` request status · `3` verify certificate ·
`4` nearest drop-off points.

Inbound SMS (`POST /api/v1/sms/inbound`, fields `from`, `to`, `text`, `id`) supports
`SELL <brand> <DEAD|SCREEN|FAULTY> <area>`, `STATUS <ref>`, `VERIFY <certificate>` and `HELP`.
Outbound SMS goes through `ISmsSender`. The bundled `FakeSmsSender` keeps messages in memory and logs only a
masked number. Swap in an Africa's Talking implementation for production. To protect webhooks, set
`Gateway__WebhookToken` and append `?token=...` to the callback URLs configured at the gateway.

With **Postman**, use `x-www-form-urlencoded` bodies. With **Rider/VS/VS Code**, open `CircuLedger.http`,
which walks the full pipeline in order.

---

## Endpoints and roles

> **Frontend developers:** see **[docs/FRONTEND_INTEGRATION_GUIDE.md](docs/FRONTEND_INTEGRATION_GUIDE.md)** for
> request/response examples, error codes, state machines, a screen-to-endpoint map, TypeScript types and a
> ready-made API client. Building the "My profile" screens (edit details, change email, phone or password)?
> See **[docs/PROFILE_MANAGEMENT.md](docs/PROFILE_MANAGEMENT.md)**.

All routes are versioned under `/api/v1`. Errors are RFC 7807 `application/problem+json` with `traceId` and a
machine-readable `code` (e.g. `insufficient_funds`, `photo_required`, `duplicate_imei`).

Policy groups: **Ops** = Admin, OperationsLead · **Workshop** = Ops + Technician · **Field** = Ops + FieldAgent ·
**Staff** = Admin, OperationsLead, Technician, FieldAgent · **Buyer** = Buyer (Ops may act on their behalf).

| Method | Route | Access | Purpose |
|---|---|---|---|
| POST | `/auth/register` | Anonymous | Self-register Buyer or Citizen (citizen links their phone) |
| POST | `/auth/login` · `/auth/refresh` · `/auth/logout` | Anonymous | JWT + rotating refresh tokens |
| GET/PUT | `/users/me` | Authenticated | Profile, incl. gender (inclusion KPI) |
| GET | `/users/me/intake-requests` | Citizen | A citizen's own sell requests |
| GET/POST | `/users` | Admin | List / create staff accounts |
| PUT | `/users/{id}/roles` · `/users/{id}/active?value=` | Admin | Role assignment, deactivate |
| POST | `/ussd/callback` | Anonymous (rate-limited, optional token) | USSD gateway webhook (`CON`/`END` text) |
| POST | `/sms/inbound` | Anonymous (rate-limited, optional token) | Inbound SMS keywords |
| GET | `/drop-off-points` · `/drop-off-points/{id}` | Staff | List (incl. inactive) / get |
| POST/PUT/DELETE | `/drop-off-points[/{id}]` | Ops | Manage drop-off points (soft delete) |
| POST | `/intake-requests` | Staff, Citizen | Create sell request (web channel) |
| GET | `/intake-requests` · `/intake-requests/{id}` | Staff | List/filter, get |
| PUT | `/intake-requests/{id}/pickup-location` | Field | Set GPS when USSD area wasn't geocoded |
| POST | `/intake-requests/{id}/cancel` · `/reject` | Ops | Close a request |
| POST | `/intake-requests/{id}/receive` | Staff | Receive at hub, creating the Device (tracking ID) |
| POST | `/intake-requests/walk-in` | Staff | Walk-in seller: request + device in one step |
| POST | `/dispatch/batches/generate` | Ops | Haversine proximity batching and agent assignment |
| GET | `/dispatch/batches` | Ops | List batches |
| GET | `/dispatch/batches/{id}` · `/dispatch/my-batches` | Field (own batches) | Batch detail / agent's open batches |
| GET | `/dispatch/batches/{id}/pickups/{reqId}/contact` | Field (assigned agent) | Reveal the seller's phone (logged) |
| POST | `/dispatch/batches/{id}/pickups/{reqId}/collect` | Field (assigned agent) | Mark pickup collected |
| GET | `/devices` · `/devices/{id}` · `/devices/tracking/{trackingId}` | Staff | Device register |
| POST | `/devices/{id}/purchase` | Ops | Buy at matrix price; debits Seed Fund (**409** if insufficient) |
| GET/PUT | `/devices/{id}/purchase` · `/purchase/payout` | Staff / Ops | Purchase & payout status (Cash/Orange Money/Afrimoney) |
| POST | `/devices/{id}/sanitizations` | Workshop | Record wipe; **Passed** issues a signed certificate and SMS |
| GET | `/devices/{id}/sanitizations` | Staff | Sanitization history |
| PUT | `/devices/{id}/recovery-outcome` | Workshop | Refurbish / DismantleForParts / HazardousDisposalOnly |
| POST | `/devices/{id}/dismantle` · `/devices/{id}/components` | Workshop + **safety training** | Dismantle, recover components |
| GET | `/recovery/components` | Staff | Component inventory |
| POST | `/recovery/hazardous-materials` | Workshop + **safety training** | Log lithium/lead/other kg, swollen flag |
| GET | `/recovery/hazardous-materials[/summary]` | Staff | Logs and kg totals |
| GET | `/pricing/matrix` | Staff | Pricing matrix (brand × condition) |
| PUT | `/pricing/matrix` | Ops | Upsert a matrix cell |
| GET | `/certificates` · `/certificates/{no}` | Staff | Certificates |
| GET | `/certificates/{no}/pdf` · `/qr` | Staff | PDF (QuestPDF) and QR PNG (QRCoder) |
| POST | `/certificates/{no}/revoke` | Admin | Revoke |
| GET | `/public/certificates/{no}/verify` | Anonymous | Validity, issue date, masked device. **No personal data** |
| GET | `/public/certificates/signing-key` | Anonymous | RSA public key PEM + key ID |
| GET | `/public/drop-off-points` · `/public/pricing/quote` | Anonymous | Locations, price estimate |
| GET | `/public/listings` · `/public/listings/{id}` | Anonymous | Browse/search/filter (brand, type, price, Samsung only), paged |
| GET | `/public/listings/{id}/photos/{photoId}` | Anonymous | Verified photos of public listings |
| POST/PUT | `/listings[/{id}]` | Ops | Create/edit listing (refurbished device needs a valid certificate) |
| GET | `/listings` · `/listings/{id}` | Staff | All listings, any status |
| POST | `/listings/{id}/photos` | Staff | Upload picture evidence (JPEG/PNG/WebP ≤5 MB, magic-byte checked) |
| POST | `/listings/{id}/photos/{photoId}/verify` | Ops (not the uploader) | Verify a photo |
| POST | `/listings/{id}/publish` · `/withdraw` | Ops | Publish (≥1 verified photo) / withdraw |
| POST | `/orders` | Buyer | Place order (atomically reserves listings) |
| GET | `/orders/mine` | Buyer | Own orders |
| GET | `/orders` | Ops | All orders |
| GET | `/orders/{id}` | Owner or Ops | Order detail incl. reinvestment split |
| POST | `/orders/{id}/pay` | Ops | Paid: items sold, fund credits and ledger entries in **one transaction** |
| POST | `/orders/{id}/fulfil` | Ops | Fulfilled |
| POST | `/orders/{id}/cancel` | Owner or Ops | Cancel pending order (releases listings) |
| GET | `/funds` | Ops | Balances, totals, transaction history |
| POST | `/funds/movements` | Admin | Grant / training expense / adjustment |
| GET | `/reinvestment-rule[/history]` | Staff | Active rule and history |
| PUT | `/reinvestment-rule` | Admin | Replace rule (versioned) |
| GET/POST | `/training/cycles` | Staff / Ops | Training cycles |
| GET/POST | `/training/cycles/{id}/enrollments` | Ops | Enrolments (gender for inclusion KPI) |
| PUT | `/training/enrollments/{id}` | Ops | Progress + **SafetyTrainingCompleted** |
| GET/POST | `/employment` | Ops | Jobs created |
| GET/POST | `/awareness-campaigns` | Staff | Schools/campuses reached, reach count |
| GET | `/dashboard/kpis` | Ops | All pilot KPIs vs targets |
| GET | `/dashboard/time-series/intake` · `/sales` | Ops | Weekly series (`from`, `to`) |
| GET | `/dashboard/export/kpis.csv` · `/weekly.csv` | Ops | CSV for M&E reports |
| GET | `/ledger` | Ops | Ledger entries (filter by entity/event) |
| GET | `/ledger/devices/{trackingId}` | Staff | Full device history |
| GET | `/ledger/verify` | Ops | Recompute the chain; report the first broken link |
| GET | `/health` (unversioned) | Anonymous | Liveness + PostgreSQL |

---

## Key design points

**Traceability ledger.** Each event (`IntakeCreated`, `DeviceReceived`, `Purchased`, `Sanitized`,
`CertificateIssued`, `Dismantled`, `ComponentRecovered`, `MaterialDiverted`, `Listed`, `Sold`, `Reinvested`) is a
`LedgerEntry` with a sequence number from a PostgreSQL sequence. The hash is
`Hash = SHA256(PreviousHash | payload | timestamp | sequence | entity | event | trackingId | actor)`, which is the
spec's formula with the metadata columns also bound in, so changing them is detected too. The payload is
canonical JSON (sorted keys), so jsonb's key reordering doesn't break verification, and timestamps are truncated
to PostgreSQL's microsecond precision. Writers take a transaction-scoped advisory lock and then
`SELECT … FOR UPDATE` the tail row. The advisory lock matters: under READ COMMITTED, a writer blocked only on
the old tail row would re-read that same row after it unlocks and fork the chain. Immutability is enforced
three times: the DbContext rejects Modified/Deleted entries, the migration REVOKEs privileges, and a trigger
rejects mutations.

**Trust Engine.** Certificate payloads are canonical JSON containing tracking ID, brand/model, IMEI last 4,
method, NIST 800-88 category, passes, times and technician reference. Each payload is SHA-256 hashed and signed
with **RS256**. The QR code links to the public verify URL. Anyone can verify offline with
`/public/certificates/signing-key`.

**Data protection.** Phone numbers are never stored in clear. They're kept as a keyed HMAC-SHA256 for lookup,
AES-256-GCM ciphertext for sending SMS, and the last 4 digits for masked display. IMEIs are stored as a keyed
HMAC plus the last 4 digits. Logs contain only IDs and masked numbers. Field agents see a seller's full number
only for their own scheduled pickup, and every reveal is logged. USSD status lookups only answer the phone that
created the request.

**Money.** Purchases lock the Seed Fund row (`FOR UPDATE`) and `Fund.Debit` refuses to go negative (there's also
a DB check constraint). Paying an order reserves listings atomically, marks them sold, applies the active
reinvestment rule (default 20% Seed + 10% Training), and writes `Sold` and `Reinvested` ledger entries, all in
one transaction.

**Safety rule.** Dismantling, component recovery and hazardous-material logging require a
`TraineeEnrollment` with `SafetyTrainingCompleted = true` for the acting user (Application layer,
`SafetyTrainingGuard`).

**Cross-cutting.** DTOs only at the boundary (Mapster plus explicit projections). FluentValidation runs via a
global action filter. Audit columns and soft delete apply to all non-ledger entities through a global query
filter. All timestamps are UTC `timestamptz` via `TimeProvider`. Rate limits cover the public, USSD/SMS and auth
endpoints. CORS origins come from `Cors:AllowedOrigins`.

### Modelling decisions (confirmed with the team)

* **Citizens** are a separate `Citizen` entity (phone hash + encrypted phone), optionally linked to an Identity
  account when they register.
* **One intake request = one device.** The `Device` and its tracking ID are created when the phone is
  received at a hub.
* **Certificates** are signed with RSA (public key published), not HMAC.
* **Orders** have line items (several listings per checkout).

### Assumptions to review

* Drop-off point and gazetteer coordinates are approximate and should be re-surveyed with GPS on site. USSD
  pickup areas are geocoded from the `Geo:Places` gazetteer. Unmatched requests are excluded from batching
  until an agent or lead sets coordinates.
* Pricing matrix values (SLE) are placeholders for the team to calibrate.
* A `Failed` payout does not refund the Seed Fund automatically; record an `Adjustment` if the purchase is
  abandoned.
* Certificate PDFs are staff-only (they name the technician). Owners get the number and verify link by SMS.
* The USSD/SMS gateway is faked. Implement `ISmsSender` for Africa's Talking before going live.
