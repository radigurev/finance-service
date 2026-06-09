# CHG-FIX-002 — Journal reversal audit row violates the BeforeJson invariant (StateChange → Create)

> Created: 2026-06-09
> Author: integration-test hardening (Batch 15 — offline gate)
> Status: Implemented
> Related specs: SDD-AUDIT-001 (Immutable Audit Trail — authoritative), SDD-FIN-002 (Journal Entry Lifecycle — consumer)
> Originating ticket: discovered by the new Journal endpoint integration suite

---

## 1. Summary

`JournalEntryService.RecordReversalAuditAsync` writes two audit rows for a reversal: one for the original entry's `Posted → Reversed` transition, and one for the brand-new sign-flipped reversal entry. The second row was recorded with `AuditOperation.StateChange` **and** `BeforeJson = null`. `AuditService.EnforceBeforeJsonInvariant` (SDD-AUDIT-001 §3) requires every non-`Create` operation to carry a non-empty `BeforeJson` and throws `ArgumentException` otherwise. The reversal entry has no prior state, so the row is logically a creation — the `StateChange`+null combination threw on **every reversal**, surfaced as `500 GENERIC_ERROR` after the state transition and sequence allocation had already run.

## 2. Motivation / root cause

Reversal is the only sanctioned way to correct a posted entry (posted entries are immutable, SDD-AUDIT-001), so a broken reversal path leaves no correction mechanism. The defect was hidden from unit tests because the test audit harness records `AuditEntry` values without enforcing the `BeforeJson` invariant the real `AuditService` enforces; two unit tests had even *asserted* the contradictory `StateChange` + null-`BeforeJson` shape, encoding the bug.

## 3. Scope

### In scope
- `RecordReversalAuditAsync`: record the new reversal entry's audit row as `AuditOperation.Create` (with `BeforeJson = null`, `AfterJson` = the posted reversal snapshot), mirroring the draft-create audit. The original entry's `Posted → Reversed` row remains `AuditOperation.StateChange` with a non-null before-snapshot.
- Correct the two unit tests that asserted the buggy shape to assert `Create` for the reversal entry's row.

### Out of scope (explicit)
- The audit-first-before-outbox ordering, retention, and DENY-grant rules (unchanged).
- The reversal entry's content/sign-flip logic (unchanged).

## 4. Behavior (Implemented — testable rules)

- A successful reversal MUST write exactly two audit rows: a `StateChange` on the original (non-null `BeforeJson`) and a `Create` on the new reversal entry (`BeforeJson` null, `AfterJson` non-null), both carrying the reason and recorded before the `JournalEntryReversedEvent` is published.
- The reverse endpoint MUST return `200 OK` with the reversal `JournalEntryDto` on success (no `500`).

## 5. Affected specs / code

| Spec / file | Change |
|---|---|
| `SDD-FIN-002` §2.6 | Clarify the reversal-entry audit row is a `Create` (no prior state), per the SDD-AUDIT-001 §3 invariant. |
| `src/Interfaces/Journal/Finance.Journal.API/Services/JournalEntryService.cs` | `RecordReversalAuditAsync` operation `StateChange` → `Create`. |
| `Finance.Journal.API.Tests` unit `JournalEntryServiceTests` | Two reversal-audit assertions corrected to `Create`. |

## 6. Testing

- Integration (real SQL Server + audit table): `JournalEndpointIntegrationTests.Reverse_CreatesSignFlippedLinkedEntry_AndFlipsOriginalToReversed` (`SDD-FIN-002`) — green post-fix.
- Unit: the two-audit-row reversal tests now assert the corrected `Create` operation.

## 7. Status

Implemented and verified. No migration, no API/event contract change.
