# Compliance position GC-2026-0814 — how the design reflects it

Group Compliance asked for written confirmation that its position is reflected in the design
before development starts. This is the draft of that confirmation: each requirement, where the
design meets it, how that is tested, and where it does not yet.

| # | Requirement | How the design meets it | Tested by | Gap |
|---|---|---|---|---|
| 1 | Personal data, documents and biometrics stay in the customer's country | One database and one blob container per market; provider and core-banking addresses per market; the work queue is in the market's own database (no shared broker); ids carry the market, so there is no global index of applicants; logs hold no personal data | `Every_market_keeps_its_data_in_its_own_database_and_container`, `Every_market_has_its_own_database_and_blob_container` | Locally all markets share one SQL Server and one Azurite; production needs them in each country. Whether IDNow's and World-Check's endpoints process data in-country is unknown. |
| 2 | Access restricted to authorised people; every access attributable to a person or system; access log kept as long as the record; no shared or generic credentials | Each service has its own SQL login with only the rights it needs, created by `Atlas.DbSetup`; no running service holds administrator rights. Staff authenticate with signed tokens naming them and their markets, and see only their markets. Every access to personal data is logged (applicant, named service, or named staff member) before the data leaves; the log is append-only for both services and deleted only with the record | `PrivilegeTests` (eight tests, including the whole flow with only the restricted logins), `ReviewAccessTests`, `Every_step_is_in_the_access_log_with_who_did_it` | A database administrator can still change the log: production should use ledger tables or write-once storage. The staff identity provider is a stand-in. |
| 3 | A possible sanctions or PEP match is reviewed by a compliance officer in the relevant market; up to 48 h; never automated or bypassed | The system never decides a possible match: it parks it (`UNDER_REVIEW`), only an officer whose token covers the market can see and decide it, a review never expires, and no account exists before the decision | `A_possible_sanctions_match_is_never_decided_by_the_system`, `An_officer_clears_a_possible_match_…`, `An_officer_from_another_market_cannot_see_or_decide_the_case`, `An_application_under_review_does_not_expire` | Officers cannot see the document images in the staff API. Match details stay in World-Check's own tool (Atlas keeps its case reference). |
| 4 | Records kept 10 years from the date of application, rejected applications and documents included | Every application carries `RetentionUntilUtc` = submission + 10 years, whatever its outcome; a background purge deletes the application, its images, decisions, queued work and access log when it passes | `The_record_is_kept_for_ten_years_…`, `Records_are_deleted_with_everything_about_them_when_retention_ends`, `Nothing_is_deleted_before_its_retention_period_ends` | Whether the access log should outlive the record is not stated. |
| 5 | Personal data deleted on request | A data protection officer records the request; the record is **restricted** at once (processing stops, the status token stops working, staff cannot see it) and deleted when its retention ends | `An_erasure_request_restricts_the_record_…`, `An_erasure_request_stops_an_application_that_is_still_being_processed`, `A_restricted_case_disappears_from_the_compliance_queue` | Points 4 and 5 conflict. Restricting rather than deleting is my reading, and needs Compliance and Legal to confirm. |
| 6 | Annex B market requirements | See below | | |

## Annex B

| Market | Requirement | In the design |
|---|---|---|
| MA, MB | Remote; personal number, 13 digits | `^[0-9]{13}$`; check digit not validated (algorithm not given) |
| MC | Remote; 9 alphanumeric characters, a letter in position 9 | `^[A-Z0-9]{8}[A-Z]$` |
| MD | Remote for the application only; branch attendance and a wet signature before activation; no exemption to be assumed | `ActivationMode: InPerson`: approved applicants wait in `AWAITING_BRANCH_ACTIVATION` until branch staff confirm the visit; nothing is opened before |
| ME | Remote; civil number, 10 digits, a different check digit | `^[0-9]{10}$`; check digit not validated (algorithm not given) |
| MF | Remote; personal number not issued to non-citizen residents; passport accepted in lieu | A national id, or a passport number instead; nationality can differ from the market |
| Note 2 | Identifiers for validation only, never a primary key | Records are keyed by a random id. Duplicates are found by a keyed hash of market and identifier, which is a lookup, not a key |
| Note 3 | A new market needs the annex reissued first | Every market has `Enabled`; a disabled market accepts no applications |

Every value above is in [`config/markets.json`](../config/markets.json); the tests load that file,
not a copy of it.
