# Decisions

The requirements contradict each other in several places: the ticket, the Teams channel, the
compliance position and its annex were written by different people, and the ticket was written
first. Where they conflict I gave **legal and regulatory requirements priority over product
promises**, kept the agreed API wherever I could, and made every departure additive rather than
breaking. Part 1 covers those conflicts; part 2 the design choices; part 3 what the tests found.

---

## Part 1 — What I did not build as described

### 1. The contract: one call, one answer, and where it breaks

**The ticket:** `POST /applications` → `201 APPROVED | REJECTED`, under 3 minutes, no polling.
**Why it cannot hold:** a World-Check possible match goes to manual review for up to 48 hours
(Teams, 19 Aug; Compliance point 3). MD requires a branch visit before activation (Annex B). A
provider outage can outlast any wait.

**What I built:** the request waits for up to 60 seconds (configurable) for the application to
settle. Decided → `201 APPROVED/REJECTED`, exactly the ticket's contract. Not decided → `202` with
`UNDER_REVIEW`, `AWAITING_BRANCH_ACTIVATION` or `PROCESSING`, a `Location` header and a status token
for `GET /applications/{id}`.

**Rejected:** *always `202` and poll* — breaks the contract for the large majority who could get an
answer at once; *hold the connection until decided* — impossible for a 48-hour review, and mobile
and ingress timeouts would turn fine applications into errors; *reject whatever is not decided in
time* — rejects people for our provider's outage, and auto-rejecting a possible match is not
allowed either.

**Cost:** mobile must handle `202` and statuses it did not expect; the slow cases need polling,
which mobile said it did not want. There is no way round that for a 48-hour human review.

### 2. "No application requires human intervention" (AC3)

Compliance point 3 requires a compliance officer to review every possible sanctions or PEP match,
in the applicant's market, and forbids automating or bypassing it. Compliance wins: the system
never decides a possible match. It parks the application, and the Review API gives officers a
queue and a decision endpoint. The customer is told `UNDER_REVIEW` — nothing about sanctions,
which could tip them off — and a declined applicant sees `DECLINED`, not why.

### 3. MD and the branch visit

Annex B: remote identification is allowed for the application, but activation needs an in-person
signature, and *no exemption may be assumed*. MD is `ActivationMode: InPerson` in configuration:
an approved MD applicant waits in `AWAITING_BRANCH_ACTIVATION`, and branch staff confirm the visit
in the staff API, after which the account and card are opened. One codebase still holds: the
difference is a configuration value that the workflow reads, not an MD branch in the code.

### 4. `nationalId` as one required string

It is 13 digits in MA/MB, 10 in MD and ME (with a different check digit), 9 characters ending in a
letter in MC, and MF residents often have none (Annex B, Teams 29 Jul). Each market's format is
configuration. `nationalId` stays where it was but is validated per market, and an optional
`passportNumber` is accepted where the market allows it (MF). The identifier is never a key
(Annex B note 2): records are keyed by a random id, and duplicates are found through a keyed hash.

### 5. Nationality is not the country

World-Check screens on nationality; the ticket sends the market code. For the residents Annex B
mentions they differ, and screening against the wrong nationality weakens AC5. An optional
`nationality` was added, defaulting to `country`, so the agreed payload still works.

### 6. "Build in retries"

Retrying without limit inside one request either breaks the 3 minutes or turns an outage into a
rejection. Each call has a short timeout and one or two immediate retries behind a circuit breaker;
beyond that the background worker retries with back-off. An application that stays unverified for
24 hours **expires** — technically, not as a judgement — and the applicant may apply again.

### 7. Data residency against "one region, one SQL instance, shared Service Bus"

Compliance point 1: personal data must not leave the customer's country. Teo's platform is one
region, one SQL instance, one shared Service Bus. I did not design for the platform as described:

- one database per market, one blob container per market, provider addresses per market;
- no broker: the work queue is a table in the market's own database, so no message carrying
  personal data crosses a border;
- application ids carry the market (`MB-…`), so a request finds the right country's database
  without a global index of applicants, which would itself be personal data held outside the
  country;
- logs carry ids and statuses, never personal data, because Seq is shared.

Locally everything runs on one SQL Server and one Azurite. Production needs these in each country,
which is a question for Platform (see open questions).

### 8. Erasure against ten-year retention

Compliance point 5 (erase on request) contradicts point 4 (keep everything, rejected applications
included, for ten years). Data-protection law normally lets a legal retention duty take priority,
but that is a legal reading, not mine to settle. So: an erasure request **restricts** the record at
once — processing stops, the status token stops working, staff can no longer see it — and every
record is **deleted** when its retention ends, erasure requested or not. Raised as an open
question.

### 9. Save and resume

Promised in Teams ("table stakes"), contradicted by the single-POST API and by the scope message of
2 September. Not built. The design I would build is in [`LIMITATIONS.md`](LIMITATIONS.md).

---

## Part 2 — Design choices

Each: what I chose, what I rejected, and why.

**Service boundaries.** Onboarding API, Review API, and the stand-ins as a separate process. The
boundary is the one the compliance documents draw: customers and staff are different actors with
different access rules and different audit needs. *Rejected:* a separate account and card service
(too thin to defend; it is an adapter in Onboarding behind an interface, calling core banking over
HTTP); separate verification and screening services (one workflow split across network hops inside
a 3-minute budget); merging Review into Onboarding (staff and customer traffic would share one
login and one deployment).

**Onboarding layered, the rest flat.** The onboarding domain (state machine, decision rules,
identifier validation) has no web or database dependency and is tested on its own, 139 tests.
The Review API and the stand-ins are thin and would not gain from layers.

**.NET 8.** What production runs.

**A queue in SQL, not a broker.** Work is a row in the market's own database, written in the same
transaction as the application. Workers on any replica take due jobs with
`UPDLOCK, READPAST`; a crashed worker's hold lapses and another takes over. *Rejected:* RabbitMQ
with an outbox (more parts, a second place personal data could leak, per-market queues anyway);
RabbitMQ without an outbox (an application saved but never processed after a crash).

**The worker does all the work; the request waits.** The request saves the application and its
job, wakes the local worker, and polls the database until the application settles or 60 seconds
pass. First attempt, retry after an outage, resumption after a crash, and the step after an
officer's decision all run the same code. *Rejected:* processing inside the request and letting the
worker pick up leftovers — two paths that must stay identical, and a race between them.

**Images in blob storage, one container per market.** The database keeps only the name and a
SHA-256, checked when the image is read back. Images are stored before the row is written, and
removed again if the row is not. *Rejected:* `VARBINARY` in SQL (bloated rows and backups); local
files (not shared between replicas).

**No Redis.** Nothing needs it; idempotency keys in a cache that can evict them would allow
duplicate applications.

**EF Core, with raw SQL for the queue claim.** Schema from the model; `rowversion` for optimistic
concurrency. *Rejected:* Dapper throughout (more code for little gain).

**Markets are configuration.** `config/markets.json`, typed, validated at start-up — a typo stops
the service rather than failing the first customer. Other configuration can override any value,
so a deployment changes one market's database or address without editing the file. *Rejected:*
markets in the database (runtime-editable compliance rules); a class per market (the six-forks
problem ATLAS-1 wants to avoid).

**Check digits: format and length only.** Annex B says MA/MB and ME have check digits but does not
give the algorithms. A pluggable seam exists and is tested; no market uses it. *Rejected:*
implementing plausible algorithms — a wrong guess rejects real customers at the door.

**IDNow's answer.** `INVALID` or no face match → rejected. `INCONCLUSIVE`, or confidence under the
market's threshold → rejected with `IDENTITY_NOT_VERIFIED_RETRY`, and the applicant can apply again
with better photographs. *Rejected:* retrying IDNow with the same images (an inconclusive answer is
about the images, not a transient fault, and each call may be paid for); sending them to the
compliance queue (that queue is for sanctions, and nobody has agreed a human identity review).

**Verification and screening in parallel.** Neither needs the other, and running them together
keeps the wait short. Every application is sent for screening (AC5), and nothing is ever opened
without a screening answer; a failed identity check, though, rejects without waiting for one.

**Idempotency.** An optional `Idempotency-Key`; without one, a hash of the request body is the key,
so even clients that know nothing about it are protected. Separately, one live application per
applicant, enforced by a filtered unique index on a keyed hash of market and identifier. A refused
duplicate gets `409` with no application id — the id would tell the caller that someone with that
identifier has applied. *Rejected:* a required header (breaks the agreed API).

**Account and card: no rollback.** Each step is recorded, idempotent on the application id, and
retried; the application is approved only when both are done. An opened account is never closed
because the card order failed. *Rejected:* compensation (closing accounts is risky and can fail
too); approving before the card (AC6 says "ordered card").

**Stand-ins driven by data.** Rules in `scenarios.json`, chosen by markers in the request,
reloaded on change. IDNow's request carries no name, so its markers go in the image. Failure counts
are per request, so one applicant's retries do not use up another's. *Rejected:* an admin API that
scripts the next responses (global state, breaks under concurrency); hard-coded values (needs a
code change, which is exactly what you said you would check).

**A setup tool and least-privilege logins.** `Atlas.DbSetup` runs once with administrator rights;
each service then has its own login with only what it needs. No running service holds
administrator rights (Compliance point 2). *Rejected:* services creating their schema at start-up
(administrator rights in every running service, and three replicas racing).

**The status token.** An HMAC of the application id with a server key, so nothing is stored and a
replayed request can be given its token again. Unknown id, unknown market and wrong token all
answer the same `404`. *Rejected:* the id alone (ids end up in logs and URLs); real customer
authentication (nothing in the material says how mobile authenticates, and an applicant has no
account yet).

**The Review API shares the market database, through a narrow contract.** It can read a
`ReviewQueue` view and add rows to `ReviewDecisions`, nothing else; the onboarding worker applies
every decision through the domain rules. Two services, one database — a smell I accepted
deliberately: the database grants make the contract enforced rather than conventional, the data
stays in-country, and no service-to-service API exposing personal data is needed. *Rejected:*
internal endpoints on Onboarding (service-to-service authentication, and a second surface returning
personal data); a separate review database (copies of personal data, doubling the residency and
erasure work).

**Staff identity.** Signed tokens (JWT) from an identity provider, with a dev-only stand-in issuer
in the stand-ins. The token names the person and their markets; staff only see their markets, and a
case from another market answers exactly like one that does not exist. *Rejected:* API keys in
configuration (static, no expiry); trusting a header from a gateway we do not have.

**The access log.** A table in each market's database. Every access to personal data is a row with
who (the applicant, `service:atlas-onboarding`, or `staff:<id>`), what and when — and no personal
data. Both service logins may add rows but not read, change or delete them, and the access is
written *before* the data leaves (for example before the call to IDNow). *Rejected:* Seq (not a
records store, shared across markets, no retention); a central audit service (a central store of
records about customers in six countries).

**Branch activation in the staff API**, under its own role, through the same decision table.

**Erasure requests by a data protection officer**, under its own role, with a reference to the
customer's request; the applicant's identity is checked through the bank's channel first. The
retention purge deletes rows through a stored procedure that only removes records past their
retention date; the onboarding login may run it but cannot delete access log rows itself.
*Rejected:* self-service with the status token (weak proof for an irreversible lock-out).

---

## Part 3 — What the tests found

Real tests against a real SQL Server found these; each is fixed and covered:

- `InvariantGlobalization` stopped SQL Server connections entirely.
- SQL Server does not allow `NOT IN` in a filtered index — the rule that one person can have only
  one live application.
- Two separate queries ("same request?", "same person?") raced: the same request sent twice at
  once was refused as a different application from the same person. Now one query.
- Two unique indexes on the same column were silently merged into one by EF, which removed "one
  review decision per case".
- An identity column on the access log would have made inserts need read permission, so
  "insert-only" was not.
- The markets file overrode environment variables, so a deployment could not change a database
  name.
- SQL Server refuses a password that contains the login name.
