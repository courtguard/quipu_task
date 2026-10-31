# Known limitations

## Not verified in this environment

I built this on a machine without Docker. Everything that needs SQL Server was verified against
**SQL Server 2019 LocalDB**, not the SQL Server 2022 container. That covers all 78 end-to-end tests,
the setup tool, both start scripts, and the services running as real processes.

- **Blob storage was never run.** The tests use an in-memory store, so
  `AzureBlobDocumentStore` and Azurite are untested. A submission through `run.ps1`/`run.sh`
  depends on them, and it is the first thing I would check with Docker.
- **`run.sh` was tested under Git Bash on Windows**, not on macOS or Linux.

## Not built

- **Save and resume.** The design I would build, additive to the current API:
  `POST /applications/drafts` with the personal details, returning a draft id and a status token;
  `PUT /applications/drafts/{id}/documents/{type}` for each image, uploaded as it is taken;
  `POST /applications/drafts/{id}/submit`, which runs the same validation and workflow as the
  single POST. Drafts would live in the market's database, keep the images in blob storage rather
  than unencrypted on the phone, and be deleted after a short period, since nothing obliges us to
  keep an unsubmitted draft.
- **Telling the customer** when a review or branch visit completes. They must ask with
  `GET /applications/{id}`; a notification would be a new step after each decision.
- **Officers viewing the images.** The staff API shows details and the World-Check reference but
  not the document or selfie.
- **Finding an application by person.** A data protection officer needs the application id; a
  search by identifier would need the hashing key or the identifier in the staff API.
- **Versioned migrations.** The schema is created with `EnsureCreated`, which does not change an
  existing database.
- **Age checks, rate limiting, HTTPS, CORS**, and a curated OpenAPI description (Swagger shows what
  the endpoints accept, without examples).

## Simplified

- **The status token is not authentication.** It proves possession of a secret, not identity. An
  applicant who loses it cannot ask about their application.
- **One worker loop polls every market** every half second when idle, and a waiting request polls
  its application every 200 ms. Fine at this scale; at volume I would add database notifications.
- **A card order that never succeeds** is retried forever, at most every five minutes. Production
  needs an alert and a manual process.
- **Duplicate detection** keys on the identifier given. An applicant who used a national id once and
  a passport number the next time is not recognised as the same person.
- **The access log is append-only for the services, not for a database administrator.** Ledger
  tables or write-once storage would make it tamper-evident.
- **Images are deleted before the rows** at the end of retention; a failure between the two leaves
  rows whose images are gone, which the next run deletes. The reverse order could orphan images
  for good.
- **The staff identity provider is a stand-in.** It signs whatever it is asked to, with a key that
  is in this repository.
- **Development secrets** (keys, passwords) are in `appsettings.Development.json`. Production would
  take them from the vault; the services refuse to start without them.

## Next, in order

1. Run everything against the Docker images, Azurite included.
2. Save and resume.
3. Customer notification after a staff decision.
4. Versioned migrations.
5. Ledger tables for the access log.
6. Images in the staff API, with each view logged.

## About the history

One commit ("Move public application ids to Shared…") does not build on its own; it builds
together with the next one.
