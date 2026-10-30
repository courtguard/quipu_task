# Open questions

Questions I would have asked during the work. Each says what I assumed meanwhile, so the answer
tells you exactly what would change.

## For Group Compliance and Legal

1. **Erasure against retention (points 4 and 5).** Is restricting a record until its ten-year
   retention ends, then deleting it, an acceptable answer to an erasure request? *Assumed: yes.*
2. **How long the access log is kept.** Point 2 says "the same period as the underlying record",
   so it is deleted with the record. Should the record of who accessed the data outlive the data?
   *Assumed: no, deleted together.*
3. **An identity failure that is also a possible match.** If IDNow rejects the document and
   World-Check reports a possible match, the application is rejected without review, because no
   account will be opened. Is a sanctions hit on a rejected applicant still reportable?
   *Assumed: no review is needed.*
4. **PEP matches.** Point 3 treats PEP like sanctions: manual review. Should a confirmed PEP be
   onboarded with enhanced due diligence rather than declined? *Assumed: the officer decides.*
5. **What a declined applicant is told.** `DECLINED`, nothing more, to avoid tipping off. *Assumed:
   correct for all markets.*

## For Product (N. Kühn) and the mobile team

6. **The `202` statuses.** Mobile will receive `202` with `UNDER_REVIEW`,
   `AWAITING_BRANCH_ACTIVATION` or `PROCESSING`, and must ask again later. Is a push notification
   expected, and from whom? *Assumed: polling `GET /applications/{id}` for now.*
7. **Two new optional fields.** `nationality` and `passportNumber`. *Assumed: acceptable, since the
   agreed payload still works without them.*
8. **Save and resume.** Promised in Teams, contradicted by the single POST. Which is it?
   *Assumed: the single POST; see [`LIMITATIONS.md`](LIMITATIONS.md) for the design.*
9. **Minimum age.** Nothing says how old an applicant must be. *Assumed: no check.*
10. **The three minutes.** For everyone, or for applicants who are not referred? *Assumed: the
    latter; a referral cannot meet it.*

## For Backend (S. Daskalov — "ping me before you commit to the data model")

11. **Check-digit algorithms** for MA/MB and ME, and the format of MF's personal number. Annex B
    names them but does not define them. *Assumed: format and length only.*
12. **What looks simple and isn't** in the data model. I found the identifier formats, the missing
    national ids, the nationality and the image sizes; I may have missed what you meant.

## For Platform (T. Radev)

13. **Residency.** Where are MA–MF, and is West Europe inside each of them? If not, one region and
    one SQL instance cannot hold the data. *Assumed: one database and one container per market,
    which can be placed per country.*
14. **The shared Service Bus.** Messages would carry or point at personal data across markets.
    *Assumed: not used; the queue lives in each market's database.*
15. **Staff identity.** Which identity provider, and do its tokens carry the staff member's markets
    and roles? *Assumed: yes, as signed tokens.*

## For the providers

16. **Idempotency.** Can a repeated IDNow or World-Check request be recognised, so a retry after a
    timeout is not a second paid check? *Assumed: not known; retries are kept few.*
17. **Liveness.** The ticket says IDNow does liveness, but its response has no liveness field.
    Is it inside `faceMatch` or `confidence`?
18. **Where the data goes.** Do IDNow and World-Check process images and personal data in the
    customer's country?
19. **The confidence threshold.** What score means "verified"? *Assumed: 0.85 for every market,
    as configuration.*
