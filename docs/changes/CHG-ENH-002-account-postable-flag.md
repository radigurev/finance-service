# CHG-ENH-002 — Expose `IsHeader` / `IsPostable` on Account read contract

> Created: 2026-06-03
> Author: spec-validator (raised during Batch 10 — Journal microservice)
> Status: Proposed
> Related specs: SDD-ACCT-001 (Chart of Accounts — authoritative), SDD-FIN-001 (Double-Entry Engine — consumer), SDD-NOM-001

---

## 1. Summary

SDD-FIN-001 §2.6 requires that a journal line may only post to a **leaf, non-header** account (`postable = active AND not a header/parent account`). The Journal microservice (Batch 10) validates account postability by reading the Accounts service through the Finance Gateway, but the `AccountDto` returned by the SDD-ACCT-001 read endpoints exposes **neither an `IsHeader`/`IsPostable` flag nor a child count**. As a result, Batch 10 ships the postability check as `exists AND IsActive` only — the leaf/non-header half of the FIN-001 MUST is currently **unenforceable through the existing contract** and is deferred to this change.

## 2. Motivation

Posting to a roll-up/header account corrupts the trial balance and the reporting hierarchy. The rule is specified (FIN-001 §2.6) and a test placeholder exists (`Validate_LineToHeaderAccount_ReturnsAccountNotPostable`, currently satisfied only via a faked reader), but production cannot detect a header account today. This change closes the gap at the source (the Accounts contract) so the Journal posting seam can enforce it with no posting-code change.

## 3. Scope

### In scope
- Add an explicit postability signal to the Accounts read contract — preferred `IsPostable` (computed = active AND leaf), optionally also surfacing `IsHeader` / `HasChildren`. Decision to be made by the spec-writer when authoring against SDD-ACCT-001.
- Update SDD-ACCT-001 (entity + read endpoint + DTO) to define and emit the flag.
- Update `AccountDto` in `Finance.ServiceModel`.
- Replace the `exists AND IsActive` check in `GatewayReferenceDataReader` (Journal) with the real leaf/postable check; the seam already accepts the future flag with no posting-code change.
- Replace the faked-reader header test with one that exercises a genuine header account.
- Remove the "Shipped-scope note (Batch 10)" deferral from SDD-FIN-001 §2.6 once enforced.

### Out of scope (explicit)
- Any change to the double-entry invariants, lifecycle, or other FIN-001/FIN-002 behavior.
- Account hierarchy editing rules (already in SDD-ACCT-001).

## 4. Affected specs / code

- `docs/domain/SDD-ACCT-001-chart-of-accounts.md` — add the postability flag definition.
- `docs/core/SDD-FIN-001-double-entry-engine.md` §2.6 — remove the deferral note once enforced.
- `AccountDto` (`Finance.ServiceModel`); `GatewayReferenceDataReader` (`Finance.Journal.API`); Accounts read endpoint/service/mapping.

## 5. Status / next step

Proposed. Pick up via the standard pipeline (spec-writer → implement → test → validate) when the Chart-of-Accounts contract is next touched, or sooner if header-account posting is observed in practice. Until then the Batch 10 deferral note in SDD-FIN-001 §2.6 documents the known gap.
