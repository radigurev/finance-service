# CHG-FIX-001 — SequenceGenerator nested transaction breaks journal posting & reversal on SQL Server

> Created: 2026-06-09
> Author: integration-test hardening (Batch 15 — offline gate)
> Status: Implemented
> Related specs: SDD-INFRA-003 (Sequence Generation — authoritative), SDD-FIN-002 (Journal Entry Lifecycle — consumer)
> Originating ticket: discovered by the new Journal endpoint integration suite

---

## 1. Summary

`JournalEntryService.PostInTransactionAsync` / `ReverseInTransactionAsync` open a DB transaction with `Db.Database.BeginTransactionAsync(...)` and then, inside it, call `ISequenceGenerator.NextAsync(...)` to allocate the gapless document number. `SequenceGenerator<TContext>.AllocateNextCounterAsync` unconditionally opened a **second** `BeginTransactionAsync(IsolationLevel.Serializable)` on the same `DbContext`/connection. SQL Server rejects nested transactions, so **every journal post and every reversal threw** `InvalidOperationException: The connection is already in a transaction and cannot participate in another transaction`, surfaced as `500 GENERIC_ERROR`.

## 2. Motivation / root cause

Posting and reversal are the core of the double-entry engine; both were non-functional against a real database. The defect was invisible to the unit suite because (a) `JournalEntryService` unit tests mock `ISequenceGenerator`, and (b) `SequenceGenerator` unit tests run on SQLite in-memory, which silently tolerates `BeginTransaction` while another transaction is open. It only manifested once the Batch-15 integration tests exercised the real path on SQL Server (Testcontainers).

## 3. Scope

### In scope
- `SequenceGenerator<TContext>.AllocateNextCounterAsync`: when an ambient transaction already exists (`Db.Database.CurrentTransaction is not null`), **enlist** in it — run the locked counter increment without beginning/committing a transaction; the caller owns commit/rollback. When there is no ambient transaction, retain the previous behavior (open + commit a serializable transaction).

### Out of scope (explicit)
- The `UPDLOCK, HOLDLOCK` locking strategy and the gapless guarantee (unchanged — the lock hints still serialize counter access regardless of the ambient isolation level).
- The document-number format and the sequence definitions.

## 4. Behavior (Implemented — testable rules)

- The generator MUST allocate the next counter inside the caller's transaction when one is already open, so number allocation is atomic with the document it numbers (no nested transaction).
- The generator MUST continue to open its own serializable transaction when invoked with no ambient transaction (standalone callers, e.g. unit tests).
- Posting a draft MUST assign a gapless `JE-...` number and succeed on SQL Server; reversal MUST create the sign-flipped posted entry with its own gapless number and succeed.

## 5. Affected specs / code

| Spec / file | Change |
|---|---|
| `SDD-INFRA-003` §2.2 | Clarify that the generator participates in an ambient transaction when present (gaplessness preserved by `UPDLOCK, HOLDLOCK`). |
| `src/Infrastructure/Sequences/Finance.Infrastructure.Sequences/SequenceGenerator.cs` | `AllocateNextCounterAsync` ambient-transaction branch. |

## 6. Testing

- Integration (real SQL Server, Testcontainers): `JournalEndpointIntegrationTests` post + reverse paths (`SDD-FIN-002`), `PostingEndpointIntegrationTests` apply→post, `GeneralLedgerEndpointIntegrationTests` trial-balance/ledger over posted entries (`SDD-FIN-003`). All green post-fix.
- Existing `SequenceGenerator` SQLite unit tests remain green (standalone path unchanged).

## 7. Status

Implemented and verified (44 integration tests green; 622 unit tests green). No migration, no API/event contract change.
