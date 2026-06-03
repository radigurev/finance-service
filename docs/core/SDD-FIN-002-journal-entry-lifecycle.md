# SDD-FIN-002 — Journal Entry Lifecycle (Draft → Posted → Reversed)

> Status: Active (Batch 10 — manual journal entries: double-entry invariants + Draft→Posted→Reversed lifecycle. Period-lock guard is an extension seam pending SDD-FIN-004; posting rules (FIN-006) and GL/trial-balance (FIN-003) are deferred.)
> Owner: Finance
> Last updated: 2026-06-03
> Category: Core
> Service: `Finance.Journal.API` — port **6004**, database `finance_journal`
> Related: SDD-FIN-001 (Double-Entry Engine — the `JournalEntry`/`JournalEntryLine` entities and invariants this lifecycle operates on), SDD-INFRA-008 (Workflow Engine — `IWorkflowEngine<JournalEntry>`, `AllowedNextStates`, guards, status history, RowVersion), SDD-INFRA-006 (transactional outbox — posting/reversal events), SDD-INFRA-009 (base service/controller, `Result<T>`), SDD-INFRA-003 (gapless `JE` numbering), SDD-AUDIT-001 (audit-first; Posted is immutable), SDD-INFRA-001 (correlation, ProblemDetails), SDD-INFRA-005 (list filtering/paging), SDD-INFRA-007 (cross-aggregate guards), SDD-INT-AUTH-001 (RBAC), SDD-ACCT-001 (postable accounts), SDD-NOM-001 (currencies), SDD-FIN-004 (Fiscal Period Management — DEFERRED; the period-lock guard is an extension seam here), SDD-FIN-003 (GL & Trial Balance — future), SDD-FIN-006 (Posting Engine — future)
> ISA-95: Level 4 (Business Planning & Logistics) — Bookkeeping

---

## 1. Context & Scope

This spec defines the **lifecycle** of a `JournalEntry`: the state machine that moves an entry through `Draft → Posted → Reversed`, the side effects each transition triggers, and the invariants enforced at each boundary. It builds directly on **SDD-FIN-001**, which defines the `JournalEntry` / `JournalEntryLine` entities and the double-entry validation surface; this spec invokes that surface and adds the lifecycle on top.

The lifecycle is implemented through `IWorkflowEngine<JournalEntry>` (SDD-INFRA-008): each state declares its `AllowedNextStates`; the engine enforces transition legality, runs cross-aggregate guards, and the calling service owns `SaveChanges`, the `RowVersion` increment, and the status-history append inside the transactional-outbox unit of work (SDD-INFRA-006).

Two principles govern the lifecycle:
1. **Posted is immutable.** Once an entry is `Posted` it MUST NEVER be UPDATEd, deleted, or have its lines edited (SDD-AUDIT-001). A correction is made by **reversing** the entry — posting a new, sign-flipped entry that links back to the original via `ReversesEntryId`. The original moves to `Reversed`; the reversal is itself a `Posted` entry.
2. **Posting is gapless and auditable.** Posting assigns the gapless `JE` document number (`ISequenceGenerator`, SDD-INFRA-003), stamps `PostedAt`/`PostedBy`, writes an `audit.OperationsEvents` row, and publishes `JournalEntryPostedEvent` — all atomically via the EF Core transactional outbox.

**Deferred dependency — fiscal period lock (SDD-FIN-004).** A journal entry MUST NOT be posted into a **closed or locked** fiscal period. SDD-FIN-004 (Fiscal Period Management) is **not yet built** (a later batch). This rule still belongs to the posting path, so this spec defines it as a **documented extension seam**: an `IPostingPeriodGuard` abstraction that the posting guard calls to ask "is the period for this `EntryDate` open?". Until SDD-FIN-004 ships, the **default implementation treats all periods as open** (`AlwaysOpenPostingPeriodGuard`) so posting works end-to-end; SDD-FIN-004 will supply the real period-status lookup and the `POSTING_PERIOD_CLOSED` rejection. This is a deliberate deferral — no periods service is invented here.

**ISA-95 classification.** A `JournalEntry` is an ISA-95 **Level 4 (Business Planning & Logistics)** business-transaction record (ISA-95 / IEC 62264 Part 1, §5). The **post** and **reverse** operations are Level-4 financial business transactions that change the entry's recorded state; each MUST emit an **immutable domain event** (`JournalEntryPostedEvent`, `JournalEntryReversedEvent`) for state changes (SDD-INFRA-006) and an immutable audit row (SDD-AUDIT-001). The `JournalEntryStatusHistory` rows are **append-only Level-4 audit sub-records** of the transaction's lifecycle (who/when/correlation per transition) and are never mutated or deleted. No Level-3 (MES) production activity is modelled. Reference data consumed (accounts, currencies) is Level-4 master data (SDD-ACCT-001, SDD-NOM-001).

**Scope — covered:**
- The `Draft → Posted → Reversed` state machine via `IWorkflowEngine<JournalEntry>` with hard `AllowedNextStates`.
- Create (draft), update (draft only), delete (draft only), post, reverse — manual/explicit entries (caller supplies lines per SDD-FIN-001).
- Posting side effects: gapless `JE` number, `PostedAt`/`PostedBy` stamp, audit-first → outbox `JournalEntryPostedEvent`, status-history append, all atomic.
- Reversal: sign-flipped new entry linked via `ReversesEntryId`; original → `Reversed`; reversal → `Posted`; mandatory `Reason`; audit-first → outbox `JournalEntryReversedEvent`.
- Immutability enforcement on `Posted` entries.
- The `IPostingPeriodGuard` extension seam (default = always open) for the deferred period-lock rule.
- List (filtered/paged), get-by-id.

**Scope — excluded (deferred):**
- **Fiscal period status lookup** — SDD-FIN-004. This spec ships the `IPostingPeriodGuard` seam + an always-open default; the real period-status check (and the actual `POSTING_PERIOD_CLOSED` enforcement) is owned by SDD-FIN-004.
- **Rule-derived posting** (turning an invoice/payment into a balanced entry via posting templates) — SDD-FIN-006. This batch covers manual entries only.
- **GL aggregation / trial balance** (account balances from posted lines) — SDD-FIN-003.
- **Approval / maker-checker workflow** before posting — future `CHG-FEAT-*` (a possible extra state between Draft and Posted); not in v1.
- **Recurring / scheduled journal entries** — future.
- Automatic exchange-rate resolution and FX revaluation — SDD-FIN-005.

## 2. Behavior

> **Service/controller contract (SDD-INFRA-009).** `JournalEntryService` inherits `SearchableServiceBase<JournalEntry, JournalEntryDto, JournalDbContext>` (and `BaseEntityService<JournalDbContext>`). Every public method MUST return `Result` / `Result<T>` — never `null`, never a thrown exception for a business outcome. `JournalEntriesController` inherits `BaseApiController` and translates every result via `ToActionResult(...)`. State transitions MUST go through `IWorkflowEngine<JournalEntry>` (SDD-INFRA-008); the service owns `SaveChanges` / `RowVersion` / status-history inside the outbox transaction.

### 2.1 State machine (MUST — SDD-INFRA-008)
- `JournalEntry` MUST be a workflow aggregate with exactly three states and these `AllowedNextStates`:
  - `Draft` → { `Posted` } (and the `Draft` entry MAY be **deleted**, which is a removal, not a transition — see §2.5).
  - `Posted` → { `Reversed` }.
  - `Reversed` → { } (terminal — a reversed original is final; the reversal entry is itself `Posted` and follows the same rules).
- Any transition not in `AllowedNextStates` MUST be rejected by the engine with `INVALID_JOURNAL_STATE_TRANSITION`. (The engine's generic code is `INVALID_STATE_TRANSITION`, SDD-INFRA-008; the Journal domain surfaces it via the domain alias `INVALID_JOURNAL_STATE_TRANSITION` in `JournalErrorCodes` to keep error responses domain-specific — see §4.)
- A new entry MUST be created in `Draft` (§2.3).

### 2.2 Workflow guards & ordering (MUST)
- Each transition MUST run its registered `IChainValidator<WorkflowContext<JournalEntry>>` guards before the move (SDD-INFRA-008 §2.2). The `Draft → Posted` transition MUST run, in order:
  1. The SDD-FIN-001 double-entry validation surface (balance, line debit-XOR-credit, no-zero, min-two-lines, account postability, currency validity) — re-run defensively at post time even though it ran at draft creation.
  2. The **`IPostingPeriodGuard`** check (§2.7) for the entry's `EntryDate`. A closed/locked period MUST short-circuit with `POSTING_PERIOD_CLOSED`. With the default always-open guard this never fails until SDD-FIN-004 ships.
- A guard failure MUST short-circuit the transition with no side effects (no number burn, no event, no audit row) and surface as `Result.Failure(...)` carrying the failing guard's code.

### 2.3 Create draft (MUST)
- `POST /api/v1/journal-entries` MUST create a `JournalEntry` in `Draft` with the caller-supplied lines.
- Requires permission `finance.journal:create`.
- The request MUST be validated against the full SDD-FIN-001 surface (balance, lines, accounts, currencies) **before** persisting; a failing draft MUST NOT be saved.
- `EntryNumber` MUST remain NULL while `Draft` (the gapless number is assigned only at posting, §2.4).
- `BaseCurrencyCode` MUST be set from configuration (`Country:BaseCurrency`) and frozen on the entry.
- `CorrelationId` MUST be captured from `ICorrelationIdAccessor`; `CreatedAt` server-side (`SYSDATETIMEOFFSET()`); `CreatedBy` from the authenticated principal.
- Draft creation MUST write an audit `Create` row (`BeforeJson = null`) — drafts are not yet legally posted, but the audit trail records the creation. (Resolved decision §7: draft create is audited for completeness; the legally-mandatory rows are post and reverse per SDD-AUDIT-001 §2.1.)
- No domain event is published on draft creation (only post/reverse publish events).

### 2.4 Post (MUST)
- `POST /api/v1/journal-entries/{id}/post` MUST transition a `Draft` entry to `Posted` via `IWorkflowEngine<JournalEntry>`.
- Requires permission `finance.journal:post`.
- The entry MUST be in `Draft`; otherwise `ENTRY_NOT_DRAFT` (an already-posted or reversed entry cannot be posted again).
- On a successful transition the service MUST, within a single SaveChanges/outbox transaction and in this order:
  1. Run the §2.2 guards (double-entry re-validation + period guard).
  2. Assign `EntryNumber` from `ISequenceGenerator.NextAsync("JE", ct)` (gapless, `UPDLOCK, HOLDLOCK`, SDD-INFRA-003). The number MUST be allocated inside the same transaction as the post (no number burn on rollback — SDD-INFRA-003 §2.4).
  3. Stamp `PostedAt = SYSDATETIMEOFFSET()` and `PostedBy` from the principal; set `Status = Posted`.
  4. Write an audit `StateChange` row (`EventType = "JournalEntryPosted"`, `BeforeJson` = draft snapshot, `AfterJson` = posted snapshot) **before** the outbox row (audit-first, SDD-AUDIT-001 §2.4).
  5. Enqueue `JournalEntryPostedEvent` to the outbox (atomic with the DB write — no `await _bus.Publish` outside the outbox, no try/catch, SDD-INFRA-006).
  6. Append the `JournalEntryStatusHistory` row (`Draft → Posted`, who/when/correlation) and increment `RowVersion` (SDD-INFRA-008 §2.4).
- The posting `Reason` is NOT required (posting a balanced entry is a routine operation; SDD-AUDIT-001's mandatory-`Reason` list is period close, reversal, permission revocation — posting is not on it).

### 2.5 Update / delete draft (MUST)
- `PUT /api/v1/journal-entries/{id}` MUST update a `Draft` entry only (description, lines). Requires `finance.journal:create`.
  - An update against a `Posted` or `Reversed` entry MUST be rejected with `CANNOT_EDIT_POSTED_ENTRY` — posted entries are immutable (§2.8).
  - The updated draft MUST re-validate against the SDD-FIN-001 surface before persisting.
  - Optimistic concurrency MUST be enforced via the base64 `RowVersion`; a stale token MUST yield `CONCURRENT_MODIFICATION` (SDD-INFRA-009 `SaveWithConcurrencyCheck`).
  - An update MUST write an audit `Update` row (`BeforeJson` = prior draft snapshot).
- `DELETE /api/v1/journal-entries/{id}` MUST hard-delete a `Draft` entry only. Requires `finance.journal:delete`.
  - A delete against a `Posted` or `Reversed` entry MUST be rejected with `CANNOT_EDIT_POSTED_ENTRY` (posted entries are never deleted — correct via reversal).
  - Deleting a draft MUST write an audit `StateChange`/delete row (`AfterJson` reflects removal). A draft has no `EntryNumber`, so no gapless number is consumed or skipped.

### 2.6 Reverse (MUST — the immutability-preserving correction)
- `POST /api/v1/journal-entries/{id}/reverse` MUST reverse a `Posted` entry. Requires permission `finance.journal:reverse`.
- The target entry MUST be `Posted`; reversing a `Draft` (`ENTRY_NOT_DRAFT` is the wrong direction — a draft is deleted/edited, not reversed) or an already-`Reversed` entry MUST be rejected with `INVALID_JOURNAL_STATE_TRANSITION`.
- A non-empty `Reason` MUST be supplied; a missing reason MUST yield `REVERSAL_REASON_REQUIRED` (reversal is on SDD-AUDIT-001's mandatory-`Reason` list).
- Reversal MUST NOT mutate the original entry's lines. Instead it MUST, within a single SaveChanges/outbox transaction:
  1. Create a **new** `JournalEntry` whose lines are the **sign-flipped** lines of the original — each original debit becomes a credit of the same amount and vice-versa (transactional and base amounts both flipped to the opposite side), preserving currency and rate. The new entry MUST satisfy the SDD-FIN-001 balance invariant by construction (a balanced entry's mirror is balanced).
  2. Set the new entry's `ReversesEntryId` to the original's `Id`, and set the new entry's `Status = Posted` (a reversal is posted immediately — it does not pass through `Draft`).
  3. Assign the new entry a fresh gapless `JE` `EntryNumber`, stamp `PostedAt`/`PostedBy`.
  4. Transition the **original** entry `Posted → Reversed` via the workflow engine (the only mutation allowed on a posted entry is this state flag + `RowVersion` + status-history; its lines and number stay intact).
  5. Write audit rows for BOTH entries: a `StateChange` (`EventType = "JournalEntryReversed"`, with the `Reason`) on the original, and a `Create`/post row on the reversal entry — audit-first, before the outbox.
  6. Enqueue `JournalEntryReversedEvent` to the outbox (carrying the original id, the reversal id, the reason; atomic with the DB write).
  7. Append status-history rows and increment `RowVersion` on the original; the reversal entry's history starts at `Posted`.

### 2.7 Posting-period guard seam (MUST — deferred dependency on SDD-FIN-004)
- The `Draft → Posted` transition MUST consult an `IPostingPeriodGuard` (or equivalently-named) abstraction: `Task<Result> EnsurePostableAsync(DateTimeOffset entryDate, CancellationToken ct)`.
- A default implementation `AlwaysOpenPostingPeriodGuard` MUST be registered that returns `Result.Success()` for every date. This makes posting work end-to-end while SDD-FIN-004 is unbuilt.
- When SDD-FIN-004 ships, it MUST supply the real implementation that looks up the fiscal period for `entryDate` and returns `Result.Failure(JournalErrorCodes.POSTING_PERIOD_CLOSED)` when the period is `Closed` or `Locked`. The seam is the ONLY extension point — no change to the posting code is required.
- `POSTING_PERIOD_CLOSED` MUST be defined in `JournalErrorCodes` now (so the seam, the error mapping, and the test stub exist) even though the default guard never returns it in this batch.

### 2.8 Immutability of posted entries (MUST — SDD-AUDIT-001)
- A `Posted` entry's lines, amounts, accounts, currencies, and `EntryNumber` MUST NEVER be UPDATEd. The only permitted mutation on a posted entry is the `Posted → Reversed` state flag (with its `RowVersion`/status-history) performed by §2.6.
- `PUT` and `DELETE` against a non-`Draft` entry MUST be rejected with `CANNOT_EDIT_POSTED_ENTRY`.
- A reversed original keeps BOTH its original posted row and its `Reversed` flag; the reversal entry is a separate posted row. Nothing is overwritten (SDD-AUDIT-001 immutability).

### 2.9 List & get (MUST)
- `GET /api/v1/journal-entries` MUST accept a `FilterRequest` and return `PagedResult<JournalEntryDto>` (SDD-INFRA-005), default-ordered by `EntryDate` descending then `Id` (the library always appends the PK as the final deterministic sort term). `PageSize` capped at 200. Requires `finance.journal:read`.
  - Filterable/sortable surface MUST be opt-in via `[Filterable]`/`[Sortable]` on `JournalEntry`: `EntryNumber`, `EntryDate`, `Status`, `BaseCurrencyCode`, `CreatedAt`.
  - The list MUST NOT be cached (journal entries are transactional data — SDD-INFRA-004 forbids caching them).
- `GET /api/v1/journal-entries/{id}` MUST return the entry with its lines, or `JOURNAL_ENTRY_NOT_FOUND` (404). Requires `finance.journal:read`. MUST NOT be cached.

### 2.10 Cross-cutting obligations (MUST)
- Every endpoint MUST be protected by `[RequirePermission("finance.journal:<action>")]` decoded via the shared `Warehouse.Auth.Shared` package (SDD-INT-AUTH-001).
- `CorrelationId` MUST flow via `ICorrelationIdAccessor`/`CorrelationIdMiddleware` and be stamped onto every published event (SDD-INFRA-001/006).
- Events MUST be `sealed record` types implementing `IFinanceEvent` in `Finance.ServiceModel/Events/Journal/`, with `required` properties + `MessageId` + `CorrelationId` + `OccurredAt`, published via the transactional outbox only (SDD-INFRA-006).
- The service MUST be traced via OpenTelemetry with the `correlation_id` Activity tag (SDD-OBS-001); logging MUST use NLog structured templates (no string interpolation).

### 2.11 Domain events (MUST — SDD-INFRA-006)
- `JournalEntryPostedEvent` MUST carry: `MessageId`, `CorrelationId`, `OccurredAt`, `JournalEntryId`, `EntryNumber`, `EntryDate`, `BaseCurrencyCode`, and the posted `Lines` (account, debit/credit, currency, rate, base amounts) — the shape anticipated in SDD-INFRA-006 §2.2.
- `JournalEntryReversedEvent` MUST carry: `MessageId`, `CorrelationId`, `OccurredAt`, `OriginalJournalEntryId`, `ReversalJournalEntryId`, `ReversalEntryNumber`, and `Reason`.
- Both MUST be published via the EF Core transactional outbox configured on `JournalDbContext`, atomic with the DB transaction. The service MUST NOT publish outside the outbox and MUST NOT wrap the publish in try/catch.

### 2.12 Edge cases (MUST)
- **Re-posting a posted entry.** `POST .../{id}/post` on a `Posted` entry MUST return `ENTRY_NOT_DRAFT` — never a second gapless number, never a duplicate event.
- **Reversing a draft.** `POST .../{id}/reverse` on a `Draft` entry MUST return `INVALID_JOURNAL_STATE_TRANSITION` (a draft is edited/deleted, not reversed).
- **Reversing an already-reversed entry.** MUST return `INVALID_JOURNAL_STATE_TRANSITION` (`Reversed` is terminal; reverse the reversal entry instead if a correction is needed).
- **Reversal without a reason.** MUST return `REVERSAL_REASON_REQUIRED` before any number is allocated or any row is written.
- **Posting into a closed period (post-FIN-004).** MUST return `POSTING_PERIOD_CLOSED`; with the Batch-10 default always-open guard this path is unreachable but the code and test stub MUST exist.
- **Concurrent post of the same draft.** Two simultaneous posts of one draft — one MUST win; the other MUST fail with `CONCURRENT_MODIFICATION` (RowVersion mismatch via `SaveWithConcurrencyCheck`).
- **Edit/delete a posted entry.** `PUT`/`DELETE` on a `Posted`/`Reversed` entry MUST return `CANNOT_EDIT_POSTED_ENTRY`.

## 3. Validation Rules

### 3.1 Field-level (FluentValidation)

| Request | Field | Rule | Error code |
|---|---|---|---|
| Create/Update | `Lines` | SDD-FIN-001 shape surface (count ≥ 2, debit-XOR-credit, no-zero, currency shape) | `MIN_TWO_LINES_REQUIRED`, `LINE_DEBIT_AND_CREDIT_SET`, `LINE_HAS_NO_AMOUNT`, `INVALID_LINE_CURRENCY` |
| Create/Update | `EntryDate` | Required | `INVALID_ENTRY_DATE` |
| Reverse | `Reason` | NotEmpty | `REVERSAL_REASON_REQUIRED` |

### 3.2 Cross-aggregate / workflow guards (SDD-INFRA-007 / SDD-INFRA-008)

| Transition | Guard | Error code |
|---|---|---|
| Draft → Posted | SDD-FIN-001 balance + postability + currency surface | `UNBALANCED_ENTRY`, `ACCOUNT_NOT_POSTABLE`, `INVALID_LINE_CURRENCY`, `INVALID_LINE_BASE_AMOUNT` |
| Draft → Posted | `IPostingPeriodGuard` (default always-open; real check via SDD-FIN-004) | `POSTING_PERIOD_CLOSED` |
| any illegal transition | `IWorkflowEngine<JournalEntry>` `AllowedNextStates` | `INVALID_JOURNAL_STATE_TRANSITION` |

### 3.3 State-based

| Condition | Rule | Error code |
|---|---|---|
| Post a non-`Draft` entry | Reject | `ENTRY_NOT_DRAFT` |
| Update/delete a non-`Draft` entry | Reject (posted is immutable) | `CANNOT_EDIT_POSTED_ENTRY` |
| Reverse a non-`Posted` entry | Reject | `INVALID_JOURNAL_STATE_TRANSITION` |
| Stale `RowVersion` on update/post/reverse | Reject | `CONCURRENT_MODIFICATION` |
| Entry not found (get/post/reverse/update/delete) | Reject | `JOURNAL_ENTRY_NOT_FOUND` |

## 4. Error Rules

All errors are RFC-7807 ProblemDetails per SDD-INFRA-001 (`title` = code, `detail` = developer English, `type` = `https://finance.local/errors/{code}`). `BaseApiController.ToActionResult` maps codes to HTTP via `IErrorCodeToStatusMap` (SDD-INFRA-009); services return `Result.Failure(code, detail)`.

| Code | HTTP | Trigger | Type |
|---|---|---|---|
| `JOURNAL_ENTRY_NOT_FOUND` | 404 | Entry id does not exist | Not found |
| `ENTRY_NOT_DRAFT` | 409 | Post attempted on a non-`Draft` entry | Conflict (state) |
| `CANNOT_EDIT_POSTED_ENTRY` | 409 | Update/delete attempted on a `Posted`/`Reversed` entry | Conflict (immutability) |
| `INVALID_JOURNAL_STATE_TRANSITION` | 409 | Transition not in `AllowedNextStates` (e.g., reverse a draft / re-reverse) | Conflict (workflow) |
| `POSTING_PERIOD_CLOSED` | 409 | `EntryDate` falls in a closed/locked period (real check deferred to SDD-FIN-004) | Conflict (period) |
| `REVERSAL_REASON_REQUIRED` | 400 | Reverse called without a non-empty `Reason` | Validation |
| `CONCURRENT_MODIFICATION` | 409 | Stale `RowVersion` on update/post/reverse | Conflict (concurrency) |
| `UNBALANCED_ENTRY` | 400 | (from SDD-FIN-001) post-time balance re-check failed | Validation (balance) |
| `ACCOUNT_NOT_POSTABLE` | 409 | (from SDD-FIN-001) line account not postable at post time | Conflict (cross-aggregate) |
| `MIN_TWO_LINES_REQUIRED` / `LINE_DEBIT_AND_CREDIT_SET` / `LINE_HAS_NO_AMOUNT` / `INVALID_LINE_CURRENCY` / `INVALID_LINE_BASE_AMOUNT` / `INVALID_ENTRY_DATE` | 400 / 409 | (from SDD-FIN-001) line/shape invariants | Validation / Conflict |

`ENTRY_NOT_DRAFT`, `CANNOT_EDIT_POSTED_ENTRY`, `INVALID_JOURNAL_STATE_TRANSITION`, and `POSTING_PERIOD_CLOSED` are all state conflicts → **409**; the `DefaultErrorCodeToStatusMap` MUST be extended to map these (none match the default `*_NOT_FOUND`/`*_CONFLICT`/`CONCURRENT_*` patterns). `REVERSAL_REASON_REQUIRED` → 400.

`INVALID_JOURNAL_STATE_TRANSITION` is the Journal-domain alias surfaced to clients for the workflow engine's generic `INVALID_STATE_TRANSITION` (SDD-INFRA-008 §4); the service translates the engine's failure code to the domain code so journal responses are self-describing. (Resolved decision §7.)

**Frontend obligation (no frontend in this batch).** Every code above MUST get a matching `errors.<CODE>` entry in BOTH `frontend/src/shared/i18n/locales/en.ts` and `bg.ts` in the same PR as the journal frontend (SDD-UI-001). Backend-only this batch; recorded for the frontend phase.

Constants live in `Finance.Common.ErrorCodes.JournalErrorCodes` (shared with SDD-FIN-001): `UNBALANCED_ENTRY`, `LINE_DEBIT_AND_CREDIT_SET`, `LINE_HAS_NO_AMOUNT`, `ACCOUNT_NOT_POSTABLE`, `INVALID_LINE_CURRENCY`, `INVALID_LINE_BASE_AMOUNT`, `INVALID_ENTRY_DATE`, `MIN_TWO_LINES_REQUIRED`, `ENTRY_NOT_DRAFT`, `CANNOT_EDIT_POSTED_ENTRY`, `INVALID_JOURNAL_STATE_TRANSITION`, `POSTING_PERIOD_CLOSED`, `REVERSAL_REASON_REQUIRED`, `JOURNAL_ENTRY_NOT_FOUND`. `CONCURRENT_MODIFICATION` is referenced from `CommonErrorCodes` (single source, SDD-INFRA-008/009) — NOT redefined.

## 5. Versioning Notes

`/api/v1/journal-entries/*` is the v1 surface: `POST` (create draft), `PUT` (update draft), `DELETE` (delete draft), `POST /{id}/post`, `POST /{id}/reverse`, `GET` (list), `GET /{id}`.

- **v1 — Initial specification (Batch 10).** Manual journal entries: `Draft → Posted → Reversed` via `IWorkflowEngine<JournalEntry>`; gapless `JE` numbering at posting; audit-first → outbox `JournalEntryPostedEvent` / `JournalEntryReversedEvent`; sign-flipped reversal linked by `ReversesEntryId`; `Posted` immutable; `IPostingPeriodGuard` seam defaulting to always-open.
- **Deferred (future versions / specs):**
  - **Period lock** — SDD-FIN-004 supplies the real `IPostingPeriodGuard` and activates `POSTING_PERIOD_CLOSED`. This is purely additive (the seam and error code already exist) — no version bump, no contract change.
  - **Rule-derived posting** — SDD-FIN-006 adds a posting endpoint that derives lines from a document; this spec's manual surface is unchanged.
  - **GL/trial balance** — SDD-FIN-003 reads posted lines; no change to this lifecycle.
  - **Approval state** (maker-checker) — a future `CHG-FEAT-*` may insert a `PendingApproval` state between `Draft` and `Posted`; that is a workflow change (new state + `AllowedNextStates` + migration) per SDD-INFRA-008 §5.
- Adding an event field is additive; changing the state set or transition semantics is breaking and requires `/api/v2/` + a `CHG-ENH-*` + an enum migration.

## 6. Test Plan

> Environment: Docker/SQL/Redis/RabbitMQ are not available — only `[Unit]` tests run by default. EF unit tests use SQLite in-memory; the workflow engine, guards, and number-assignment logic are testable without a real broker (the outbox publish is asserted via the MassTransit in-memory test harness; the gapless generator via SQLite). `WebApplicationFactory` HTTP tests + real-SQL `UPDLOCK`/outbox-ordering tests carry `[Category("Integration")]` and are excluded from the default run. All business tests MUST reference `[Category("SDD-FIN-002")]`.

### 6.1 State machine & guards (Unit)

| Test name | Kind |
|---|---|
| `Post_DraftEntry_TransitionsToPosted` | [Unit] |
| `Post_NonDraftEntry_ReturnsEntryNotDraft` | [Unit] |
| `Post_UnbalancedDraft_ReturnsUnbalancedEntry_NoNumberAllocated` | [Unit] |
| `Post_ClosedPeriod_ReturnsPostingPeriodClosed_WhenGuardRejects` | [Unit] |
| `Post_WithDefaultAlwaysOpenGuard_Succeeds` | [Unit] |
| `Reverse_PostedEntry_TransitionsOriginalToReversed` | [Unit] |
| `Reverse_DraftEntry_ReturnsInvalidJournalStateTransition` | [Unit] |
| `Reverse_AlreadyReversedEntry_ReturnsInvalidJournalStateTransition` | [Unit] |
| `Reverse_WithoutReason_ReturnsReversalReasonRequired` | [Unit] |
| `Workflow_DraftAllowsOnlyPosted_PostedAllowsOnlyReversed_ReversedTerminal` | [Unit] |
| `Update_PostedEntry_ReturnsCannotEditPostedEntry` | [Unit] |
| `Delete_PostedEntry_ReturnsCannotEditPostedEntry` | [Unit] |
| `Delete_DraftEntry_RemovesEntry` | [Unit] |

### 6.2 Posting side effects (Unit — SQLite in-memory + MassTransit test harness)

| Test name | Kind |
|---|---|
| `Post_AssignsGaplessJeNumber_FromSequenceGenerator` | [Unit] |
| `Post_StampsPostedAtAndPostedBy` | [Unit] |
| `Post_RecordsAuditStateChange_BeforeOutboxPublish` | [Unit] |
| `Post_PublishesJournalEntryPostedEvent_WithCorrelationIdAndLines` | [Unit] |
| `Post_AppendsStatusHistoryRow_DraftToPosted` | [Unit] |
| `Post_DoesNotPublishEvent_WhenGuardFails` | [Unit] |

### 6.3 Reversal side effects (Unit)

| Test name | Kind |
|---|---|
| `Reverse_CreatesSignFlippedNewEntry_LinkedViaReversesEntryId` | [Unit] |
| `Reverse_NewEntryIsBalanced_ByConstruction` | [Unit] |
| `Reverse_DoesNotMutateOriginalLines` | [Unit] |
| `Reverse_OriginalAuditStateChange_CarriesReason` | [Unit] |
| `Reverse_PublishesJournalEntryReversedEvent_WithOriginalAndReversalIds` | [Unit] |
| `Reverse_AllocatesFreshGaplessNumber_ForReversalEntry` | [Unit] |

### 6.4 Create / list / get / validation (Unit)

| Test name | Kind |
|---|---|
| `CreateDraft_ValidBalancedEntry_PersistsInDraft_WithNullEntryNumber` | [Unit] |
| `CreateDraft_SetsBaseCurrencyFromConfiguration` | [Unit] |
| `CreateDraft_RecordsAuditCreate` | [Unit] |
| `UpdateDraft_StaleRowVersion_ReturnsConcurrentModification` | [Unit] |
| `Get_ReturnsNotFound_WhenEntryDoesNotExist` | [Unit] |
| `Search_ReturnsPagedResultOrderedByEntryDateDescending` | [Unit] |
| `Search_DoesNotCacheTransactionalData` | [Unit] |
| `JournalErrorCodes_DefinesPostingPeriodClosed_ForDeferredFin004Seam` | [Unit] |

### 6.5 Endpoint & wiring (Integration — `[Category("Integration")]`, excluded from default run)

| Test name | Kind |
|---|---|
| `Create_Returns201_AndPersistsDraft` | [Integration] |
| `Post_Returns200_AndWritesOutboxAndAuditRow_InSameTransaction` | [Integration] |
| `Post_ConcurrentCallers_OneFailsWithConcurrentModification` | [Integration] |
| `Post_AllocatesGaplessJeNumbers_UnderConcurrency_NoGaps` | [Integration] |
| `Reverse_Returns200_AndPersistsReversalEntry_AndFlipsOriginalToReversed` | [Integration] |
| `Reverse_Returns400_WhenReasonMissing` | [Integration] |
| `Post_Returns409_WhenAlreadyPosted` | [Integration] |
| `Update_Returns409_WhenEntryPosted` | [Integration] |
| `Endpoint_Returns403_WhenPermissionMissing` | [Integration] |

## 7. Resolved Decisions & Open Items

### Resolved
- **State set & transitions.** `Draft → Posted → Reversed`; `Draft` may be deleted; `Posted` immutable except for the `Posted → Reversed` flag; `Reversed` terminal. Implemented via `IWorkflowEngine<JournalEntry>` (SDD-INFRA-008) with the calling service owning `SaveChanges`/`RowVersion`/status-history inside the outbox transaction.
- **Reversal mechanics.** Sign-flipped new posted entry linked via `ReversesEntryId`; original lines never mutated; mandatory `Reason`; both entries audited; `JournalEntryReversedEvent` published via outbox. Matches SDD-INFRA-008 §2.5.
- **Gapless numbering.** `JE` key from `ISequenceGenerator` allocated at post time inside the post transaction (no burn on rollback). Draft has NULL `EntryNumber`.
- **Domain error alias.** The engine's generic `INVALID_STATE_TRANSITION` is surfaced to journal clients as `INVALID_JOURNAL_STATE_TRANSITION` (the service translates the code) so responses are domain-specific. Both are 409.
- **Draft creation is audited** (`Create`) for completeness even though only post/reverse are on SDD-AUDIT-001's mandatory list.

### Open / deferred (for the Phase-2 implementator)
- **`IPostingPeriodGuard` seam (deferred dependency on SDD-FIN-004).** Batch 10 ships the abstraction + `AlwaysOpenPostingPeriodGuard` default + the `POSTING_PERIOD_CLOSED` code + a test stub. SDD-FIN-004 supplies the real period-status lookup and activates the rejection. The implementator MUST register the always-open default in DI now and MUST NOT invent a periods service. When SDD-FIN-004 lands, only the DI registration of the guard changes — the posting code is untouched.
- **Cross-service account/currency lookup at post time.** Shared with SDD-FIN-001 §7: the postability/currency guards need account/currency state, but the Journal service owns only `finance_journal` (no cross-DB joins, Plan §8). The implementator MUST choose the lookup mechanism (Refit-through-gateway read, locally-cached reference snapshot fed by Account/Currency events, or a denormalized reference table). Decide once and apply to both FIN-001 and FIN-002 validators.
- **Event line payload size.** `JournalEntryPostedEvent` carries all lines; for very large entries confirm the outbox/message-size limits are acceptable, or carry a line-count + id and let consumers re-read. Default this batch: embed the lines (matches SDD-INFRA-006 §2.2 anticipated shape).
- **Maker-checker approval** between `Draft` and `Posted` — out of scope for v1; track as a future `CHG-FEAT-*` if required by BG controls.
