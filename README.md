# Atlas — digital onboarding backend

A working backend for Atlas: a customer submits their details, an identity document and a selfie,
and gets back an answer. In the common case that answer is "approved, account open, card ordered"
in a single call. Where the requirements make a single answer impossible — a possible sanctions
match that must be reviewed by a person, a market where the law requires a branch visit, a
provider that is down — the call says so honestly and the work carries on in the background.

**Read [`WHERE-TO-LOOK.md`](WHERE-TO-LOOK.md) first.** The reasoning is in
[`docs/DECISIONS.md`](docs/DECISIONS.md), the compliance mapping in
[`docs/COMPLIANCE.md`](docs/COMPLIANCE.md), and what is missing in
[`docs/LIMITATIONS.md`](docs/LIMITATIONS.md) and [`docs/OPEN-QUESTIONS.md`](docs/OPEN-QUESTIONS.md).

---

## Run it

**Needs:** Docker, and the .NET 8 SDK (any newer SDK works too, with the .NET 8 runtime).
Only the allowed images are used: SQL Server, Seq and Azurite.

```powershell
./run.ps1          # Windows
```
```bash
./run.sh           # macOS / Linux
```

Either script starts the containers, builds, creates the databases and service logins, and starts
the three services. Ctrl+C stops the services (`docker compose -f platform/docker-compose.yml down`
stops the containers).

<details>
<summary>The same, by hand</summary>

```bash
docker compose -f platform/docker-compose.yml up -d     # SQL Server, Seq, Azurite
dotnet build Atlas.sln
dotnet run --project src/Atlas.DbSetup                   # once SQL Server accepts connections
dotnet run --project src/Atlas.Stubs                     # :5300  stand-ins for the providers
dotnet run --project src/Onboarding/Atlas.Onboarding.Api # :5100  the customer-facing API
dotnet run --project src/Atlas.Review.Api                # :5200  the staff-facing API
```
If port 1433 is taken: `SQL_PORT=14330 docker compose up -d`, then pass
`--Admin:ConnectionString="Server=localhost,14330;..."` to DbSetup and set
`Sql__ConnectionString` for the two APIs.
</details>

| | Address |
|---|---|
| Onboarding API | http://localhost:5100/swagger |
| Review API (staff) | http://localhost:5200/swagger |
| Stand-ins (IDNow, World-Check, core banking, staff sign-in) | http://localhost:5300/swagger |
| Logs | Seq, http://localhost:5341 (local-only, no sign-in) |

**Tests:** `dotnet test`. The end-to-end tests need a SQL Server: they use the Docker one if it
is running, otherwise Windows LocalDB, otherwise they are skipped and say why. They start the real
APIs in-process with the real stand-ins.

---

## Try it

Sample requests are in [`samples/`](samples). Each is one scenario:

```bash
curl -s -X POST http://localhost:5100/applications \
     -H 'Content-Type: application/json' --data-binary @samples/approved-mb.json
```

| Sample | Answer |
|---|---|
| `approved-mb.json` | `201 APPROVED` — account opened, card ordered |
| `rejected-retry-mb.json` | `201 REJECTED`, reason `IDENTITY_NOT_VERIFIED_RETRY` |
| `under-review-mb.json` | `202 UNDER_REVIEW` — a compliance officer must decide |
| `branch-visit-md.json` | `202 AWAITING_BRANCH_ACTIVATION` — MD requires a branch visit |
| `passport-only-mf.json` | `201 APPROVED` — MF resident without a national id |
| `idnow-down-mb.json` | `202 PROCESSING` after 60 s — never rejected for an outage |
| `card-down-mb.json` | `202 PROCESSING` — account open, card order retrying |

Sending the same file twice returns the same application (that is the idempotency). To submit a
new one, change the `nationalId`.

The response carries a `statusToken`. Ask about the application later with
`GET /applications/{applicationId}` and the header `X-Status-Token: <token>`.

### Staff: reviews, branch visits, erasure

Staff sign in at the stand-in identity provider and use the Review API with the token (the
example uses `jq`; without it, copy `accessToken` from the response by hand):

```bash
TOKEN=$(curl -s -X POST http://localhost:5300/idp/token -H 'Content-Type: application/json' \
  -d '{"subject":"officer-1","role":"compliance_officer","markets":["MB"]}' | jq -r .accessToken)

curl -s http://localhost:5200/reviews -H "Authorization: Bearer $TOKEN"
curl -s http://localhost:5200/reviews/MB-... -H "Authorization: Bearer $TOKEN"
curl -s -X POST http://localhost:5200/reviews/MB-.../decision -H "Authorization: Bearer $TOKEN" \
     -H 'Content-Type: application/json' -d '{"decision":"APPROVE","note":"checked in World-Check"}'
```

| Role | Can |
|---|---|
| `compliance_officer` | `GET /reviews`, `GET /reviews/{id}`, `POST /reviews/{id}/decision` (`APPROVE` / `REJECT`) |
| `branch_staff` | `GET /branch-activations`, `GET /branch-activations/{id}`, `POST /branch-activations/{id}` |
| `data_protection_officer` | `POST /erasure-requests/{id}` with `{"reference":"..."}` |

Staff only ever see their own markets. The decision is applied by the onboarding service within a
second; follow it with the customer's `GET /applications/{id}`.

In the Review API's Swagger page (http://localhost:5200/swagger), **Authorize** takes the
`accessToken` (without "Bearer"), and every call from the page then carries it.

### Driving the stand-ins

The stand-ins' behaviour lives in [`src/Atlas.Stubs/scenarios.json`](src/Atlas.Stubs/scenarios.json).
It reloads on every change, so new scenarios need no code and no restart. A scenario is chosen by
a marker in the request:

| Provider | Marker goes in | Markers |
|---|---|---|
| IDNow | the document or selfie image (base64 of text containing the marker) | `scenario:invalid-document`, `scenario:face-mismatch`, `scenario:inconclusive`, `scenario:low-confidence`, `scenario:slow`, `scenario:very-slow`, `scenario:down`, `scenario:flaky` |
| World-Check | the applicant's name | `Possible-Match`, `Pep-Match`, `Wc-Slow`, `Wc-Very-Slow`, `Wc-Down`, `Wc-Flaky` |
| Core banking | the applicant's name | `Account-Slow`, `Account-Down`, `Account-Flaky`, `Card-Slow`, `Card-Down`, `Card-Flaky` |

`flaky` fails the first two calls for the same request and then succeeds;
`POST /admin/reset` starts the counts over. No marker gives the happy path.

---

## What is built

Three services and a setup tool, one codebase for all six markets:

- **Onboarding API** (`src/Onboarding`, layered: Api / Domain / Infrastructure) — the customer
  API, the application state machine, provider calls, account and card opening, the background
  worker, the access log, and the retention purge.
- **Review API** (`src/Atlas.Review.Api`) — the staff API: compliance reviews, branch
  activations, erasure requests. Signed tokens, scoped to the staff member's markets.
- **Stand-ins** (`src/Atlas.Stubs`) — IDNow, World-Check, core banking, and a dev-only identity
  provider for staff, all driven by `scenarios.json`.
- **DbSetup** (`src/Atlas.DbSetup`) — creates one database per market, the schema, and a
  least-privilege login for each service.

Market differences — identifier formats, the MD branch visit, the MF passport alternative,
thresholds, databases, provider addresses — are all in [`config/markets.json`](config/markets.json).

### The API, as built

`POST /applications` takes the body agreed with the mobile team in ATLAS-1, unchanged, plus two
optional fields (`nationality`, `passportNumber`) and an optional `Idempotency-Key` header.

| Status | Meaning |
|---|---|
| `201` + `APPROVED` / `REJECTED` | decided, as the ticket promised |
| `202` + `UNDER_REVIEW` | a compliance officer must decide (can take up to 48 h) |
| `202` + `AWAITING_BRANCH_ACTIVATION` | approved in a market that requires a branch visit |
| `202` + `PROCESSING` | still working, typically a provider being slow or down |
| `400` | validation failed; field codes, never the submitted values |
| `409` | same idempotency key with a different body, or an applicant who already has a live application |

Later, `EXPIRED` means an application could not be completed in 24 hours (never a rejection of the
applicant), and rejected or expired applicants may apply again.

## What is left out

Save-and-resume, notifications to the customer, officer access to the document images, versioned
database migrations, and everything listed in [`docs/LIMITATIONS.md`](docs/LIMITATIONS.md).
