# CHG-DEBT-001 — ISA-95 guide has no role for a persisted read projection or a non-posting matching record

> Created: 2026-08-05
> Author: isa95-validate (Batch 17 — Phase 5 Payments)
> Status: Proposed
> Related specs: `.claude/context/isa95.md` (authoritative guide), SDD-PAY-002 (Payment Allocation & Settlement), SDD-PAY-003 (AP/AR Aging & Counterparty Balances), SDD-FIN-003 (General Ledger & Trial Balance), SDD-EVTLOG-001 (Centralized Event Log)
> Originating ticket: raised by the Batch-17 ISA-95 validation pass (verdict: Level-4 compliant; these are gaps in the guide, not in the specs)

---

## 1. Summary

`.claude/context/isa95.md` §2 defines five Level-4 object roles. Batch 17 legitimately introduced two constructs that fit none of them, so a strict literal reading of §4 hard rules 1 and 4 flags two correctly-designed entities. The guide is what should move: per its own §7 the guide wins over a spec, so leaving the gap open means the next validation pass re-raises the same two findings, or — worse — a future author contorts a design to fit a taxonomy that is simply incomplete.

## 2. Motivation

Two Batch-17 entities are sound, were classified explicitly and carefully argued in their specs, and were confirmed compliant in substance by the ISA-95 pass — yet neither maps onto a §2 role:

1. **`InvoiceOpenItem`** (`src/Databases/Finance.Payments.DBModel/Models/InvoiceOpenItem.cs`, SDD-PAY-002 §1/§2.2) is a persisted Level-4 **read projection** of another service's Document, fed only by that Document's immutable events, with no event and no audit row of its own. It is the repo's first persisted projection *table* — `SDD-FIN-003`'s GL and trial balance are computed on read, so no table existed to classify before now.

2. **`PaymentAllocation`** (SDD-PAY-002 §1 principle 1, §2.6) is a non-posting, deliberately mutable and removable **sub-ledger matching record**. §2's nearest role is *transaction line / component*, whose exemplar `JournalEntryLine` is immutable, so §4 rule 4 flags the deletability. But allocation posts nothing, moves no GL or trial-balance figure, and every allocate and deallocate leaves an immutable `audit.OperationsEvents` row plus an immutable outbox event — so the *history* is append-only and reconstructible even though the *current-state* row is removable. SDD-PAY-003 §2.3 discloses the resulting limitation honestly.

## 3. Scope

Documentation only. No code, no schema, no API, no event contract, no frontend change. Confined to `.claude/context/isa95.md`.

## 4. Proposed Behavior

Extend `.claude/context/isa95.md`:

- **§2 — add role (a): derived read projection / reporting view.** Persisted or computed. Fed exclusively from another aggregate's immutable events or from a read-only aggregation over records that are already classified. Owns no business transaction, emits no domain event, and writes no audit row of its own; its authority is always another aggregate. Every column has a single declared writer. Exemplars: `InvoiceOpenItem` (persisted), `SDD-FIN-003` GL/trial balance (computed), `SDD-EVTLOG-001` `EventLogEntry` (archive).
- **§2 — add role (b): sub-ledger matching record.** Links two already-classified Level-4 Documents without posting anything. May be mutable and removable, because its history of record is the append-only audit row plus the immutable outbox event, not the row itself. Exemplar: `PaymentAllocation`.
- **§3** — add the corresponding operation-mapping rows: projection maintenance (event-fed, no audit row) and sub-ledger match/release (audit row + event, no GL effect).
- **§4 rule 4** — scope it explicitly to *posted / committed* records, so a non-posting matching record is not caught by the immutability rule intended for the ledger.

Retro-classification is expected to be clean: `SDD-FIN-003` and `SDD-EVTLOG-001` should both fall under role (a) with no spec edits needed.

## 5. Affected Specs

| Spec | Change |
|---|---|
| `.claude/context/isa95.md` | §2 (+2 roles), §3 (+2 rows), §4 rule 4 (scoping clause) — the whole of this change |
| SDD-PAY-002 | None expected; its §1 classification paragraph should simply cite the new role names once they exist |
| SDD-PAY-003, SDD-FIN-003, SDD-EVTLOG-001 | None expected; retro-covered by role (a) |

## 6. Database Changes

None.

## 7. API Changes

None.

## 8. Event Contract Changes

None.

## 9. Frontend Impact

None.

## 10. Testing

Not test-bearing. Verification is a re-run of `isa95-validate` over SDD-PAY-002 and SDD-PAY-003, which MUST come back with no role-classification findings.

## 11. Rollout

Land before the next batch that introduces a projection or a matching record — Phase 7 Reporting (`SDD-RPT-*`) is the likely next one, and it will add several read-side surfaces. Not a prerequisite for the Batch-17 commit: the ISA-95 pass returned Level-4 compliant, and both entities are already classified explicitly in their own specs.

## 12. Risks

Low. Widening a taxonomy cannot invalidate an existing classification. The only real risk is the opposite of acting: role (a) is broad enough to be used as an excuse for a projection that quietly becomes a second source of truth, so its definition MUST keep the "authority is always another aggregate" and "single declared writer per column" clauses that make it falsifiable.

## 13. Open Questions

- Should role (a) distinguish persisted from computed projections? Batch 17 suggests not — the ISA-95-relevant property is the absence of independent authority, not the storage strategy — but Phase 7 Reporting will be the real test, since materialized reporting views are already a deferred item in `SDD-FIN-003` §7.
- `SDD-PAY-001` §1 describes `Payment` as a "financial Document" where §2's role name is *business-transaction record*. Cosmetic, and it mirrors the shipped `SDD-INV-001` wording that §5 house style tells new specs to match — so if it is tightened, tighten both specs or neither.
