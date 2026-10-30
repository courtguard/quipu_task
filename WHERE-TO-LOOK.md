# Where to look

**The files I most want you to read**

1. [`ApplicationProcessor.cs`](src/Onboarding/Atlas.Onboarding.Infrastructure/Processing/ApplicationProcessor.cs)
   — the whole workflow in one place: verification and screening side by side, staff decisions,
   account and card. Every step records its result before the next, and every outside call can
   be repeated safely, so a crash, a timeout or an outage loses nothing.
2. [`Application.cs`](src/Onboarding/Atlas.Onboarding.Domain/Applications/Application.cs) with
   [`ApplicationStatusRules.cs`](src/Onboarding/Atlas.Onboarding.Domain/Applications/ApplicationStatusRules.cs)
   and [`VerificationDecider.cs`](src/Onboarding/Atlas.Onboarding.Domain/Verification/VerificationDecider.cs)
   — the rules: a possible sanctions match is never decided by the system, an outage never
   rejects anyone, nothing is approved without an account and a card.
3. [`ApplicationSubmitter.cs`](src/Onboarding/Atlas.Onboarding.Infrastructure/Submission/ApplicationSubmitter.cs)
   — one application per request and per person, even when the same request arrives eight times
   at once on two replicas.
4. [`MarketDatabaseProvisioner.cs`](src/Onboarding/Atlas.Onboarding.Infrastructure/Provisioning/MarketDatabaseProvisioner.cs)
   — the boundary between the two services and the compliance controls, written as database
   grants: what each service may read, change, or only append.

**The decision I most want to be asked about**

The response contract. ATLAS-1 promises one call, one answer (`APPROVED` or `REJECTED`) in under
three minutes, with no human involved. Group Compliance requires a person to review every possible
sanctions match, which can take 48 hours, and MD requires a branch visit. I kept the ticket's
contract for everyone it can be kept for, and answer `202` with an honest status for everyone else
— rather than rejecting people the system is not allowed to reject.
[`docs/DECISIONS.md`](docs/DECISIONS.md#1-the-contract-one-call-one-answer-and-where-it-breaks).

**What you can skip**

- `src/Atlas.Stubs` beyond `scenarios.json`: test tooling, written to be driven, not admired.
- `tests/**/Support`: fixtures and helpers.
- The development keys and passwords in `appsettings.Development.json` and `src/Atlas.Stubs`:
  deliberately public, local only.
