# SDD-FIN-004 — Fiscal Period Management (Open → Closed → Reopened)

> Status: Implemented (Batch 11 — fiscal-period core model + lifecycle (`Open ⇄ Closed`) via `IWorkflowEngine<FiscalPeriod>`; period generation per fiscal year; the date→period status lookup that **fulfills** the SDD-FIN-002 `IPostingPeriodGuard` seam (activating the dormant `POSTING_PERIOD_CLOSED` rule); reference-data caching of period status; `FiscalPeriodClosedEvent`/`FiscalPeriodReopenedEvent` via the transactional outbox. **Deferred:** country-aligned (non-calendar) fiscal-year start + 13th/adjustment period → SDD-CTRY-001; FX revaluation/period-end valuation at close → SDD-FIN-005; year-end close / retained-earnings roll-forward → a later batch; the period-close *checklist* (sub-ledger reconciliation gates) → a later batch.)
> Owner: Finance
> Last updated: 2026-06-03
> Category: Core
> Service: `Finance.Periods.API` — port **6002**, database `finance_periods`, schema `periods`
> Related: SDD-FIN-002 (Journal Entry Lifecycle — **this spec fulfills its `IPostingPeriodGuard` extension seam** and activates the dormant `POSTING_PERIOD_CLOSED` rejection; see §2.7 and §7), SDD-INFRA-008 (Workflow Engine — `IWorkflowEngine<FiscalPeriod>`, `AllowedNextStates`, guards, status history, RowVersion), SDD-AUDIT-001 (immutable audit trail — close/reopen are SENSITIVE ops requiring a `Reason`), SDD-INFRA-004 (Redis cache — period status is reference data; cache + invalidate on state change; fall through to DB when Redis is down), SDD-INFRA-006 (transactional outbox + idempotency — close/reopen events), SDD-INFRA-009 (base service/controller, `Result<T>`), SDD-INFRA-005 (list filtering/paging), SDD-INFRA-007 (cross-aggregate / workflow guards — out-of-order close, no-overlap), SDD-INFRA-001 (correlation, ProblemDetails, error-code mapping), SDD-INT-AUTH-001 (shared JWT auth / RBAC), SDD-OBS-001 (observability), SDD-NOM-001 / SDD-FIN-002 (the Refit-through-gateway `GatewayReferenceDataReader` cross-service-read convention this spec's guard follows), SDD-CTRY-001 (future — country fiscal-year start), SDD-FIN-005 (future — FX revaluation at close)
> ISA-95: Level 4 (Business Planning & Logistics) — accounting-calendar reference data + period-close business activity

---

## 1. Context & Scope

A **fiscal period** is the accounting-calendar window (e.g. a calendar month) into which financial transactions are recorded. The single most important rule in any ledger is that **a transaction MUST NOT be posted into a period that has been closed** — once a period is closed and its statements are filed, its balances are frozen. SDD-FIN-002 (Journal Entry Lifecycle) already encodes this rule as a deferred extension seam: its `Draft → Posted` workflow consults an `IPostingPeriodGuard`, but Batch 10 shipped only the `AlwaysOpenPostingPeriodGuard` default and a dormant `POSTING_PERIOD_CLOSED` error code, because no Periods service existed yet. **This spec builds that service and turns the seam on.**

`Finance.Periods.API` owns the `finance_periods` database (database-per-service, Plan §8). It is a reference-data service in the canonical shape of `Finance.Accounts.API` (SDD-ACCT-001): it inherits the shared base service/controller helpers (SDD-INFRA-009), exposes a filtered/paged list (SDD-INFRA-005), enforces cross-aggregate rules through workflow guards (SDD-INFRA-007 / SDD-INFRA-008), caches reference reads with invalidation (SDD-INFRA-004), publishes domain events through the transactional outbox (SDD-INFRA-006), writes immutable audit rows (SDD-AUDIT-001), and is traced via OpenTelemetry (SDD-OBS-001).

Two responsibilities distinguish it from a plain CRUD reference service:
1. **Lifecycle.** A `FiscalPeriod` is a workflow aggregate with two states, `Open` and `Closed`, driven by `IWorkflowEngine<FiscalPeriod>` (SDD-INFRA-008). **Closing** and **reopening** are SENSITIVE operations (SDD-AUDIT-001): each MUST require a non-empty `Reason`, write an audit row in the same transaction, and publish an immutable domain event via the outbox.
2. **Date→status lookup (the integration that fulfills SDD-FIN-002).** The service exposes a lookup that, given a date, returns the containing period and whether it is `Open` or `Closed`. The Journal service consumes this through a new `GatewayPostingPeriodGuard` (Refit-through-gateway, the same convention as Batch 10's `GatewayReferenceDataReader`), replacing `AlwaysOpenPostingPeriodGuard` — so posting into a closed (or missing) period now returns `POSTING_PERIOD_CLOSED`.

**Resolved design decisions (see §7 for rationale):**
- **`FiscalYear` is a column on `FiscalPeriod`, not a separate aggregate** (v1). Periods are generated per year; a standalone `FiscalYear` aggregate (with its own year-open/year-close lifecycle and retained-earnings roll-forward) is deferred to a later batch.
- **Two states only: `Open` and `Closed`.** Reopen is the `Closed → Open` transition (not a distinct `Reopened` state); the audit trail and `FiscalPeriodReopenedEvent` record that the open was a reopen. A separate hard `Locked`/`Permanently-Closed` state is deferred.
- **Calendar-aligned monthly periods (`PeriodNumber` 1–12) in v1.** Non-calendar fiscal-year start and a 13th/adjustment period are country-strategy concerns deferred to SDD-CTRY-001 (the generation path is written behind that seam).
- **Out-of-order close is rejected (MUST).** A period MUST NOT be closed while an earlier `Open` period exists in the same fiscal year (`CANNOT_CLOSE_OUT_OF_ORDER`). Reopen has the symmetric rule (MUST NOT reopen while a *later* period is `Closed`).
- **No period for a date is a hard, distinct failure (`NO_PERIOD_FOR_DATE`)** — never silently treated as "closed" — so a misconfigured calendar is visible rather than blocking posting opaquely.
- **The Journal guard fails closed.** When the Periods lookup is unreachable, the `GatewayPostingPeriodGuard` treats the period as not-postable (blocks posting), matching the Batch-10 `GatewayReferenceDataReader` fail-closed convention — financial safety over availability.

**ISA-95 classification.** A `FiscalPeriod` is an ISA-95 **Level 4 (Business Planning & Logistics)** accounting-calendar reference/master-data artifact (ISA-95 / IEC 62264 Part 1, §5 — Level 4 business-planning information). Period **generation** is reference-data maintenance. Period **close** and **reopen** are Level-4 business-planning activities that change the recorded state of the accounting calendar; each MUST emit an **immutable domain event** for the state change (SDD-INFRA-006) and an immutable audit row (SDD-AUDIT-001). The `FiscalPeriodStatusHistory` rows are **append-only Level-4 audit sub-records** of the period's lifecycle (who/when/correlation/reason per transition) and are never mutated or deleted. No Level-3 (MES) production activity is modelled.

**Scope — covered:**
- The `FiscalPeriod` entity + `FiscalPeriodStatusHistory` sub-record (EF Core, Fluent API only, schema `periods`, INT IDENTITY PK).
- Period **generation** for a fiscal year (12 calendar-aligned monthly periods in one call) and read endpoints (list filtered/paged, get-by-id).
- The `Open ⇄ Closed` lifecycle via `IWorkflowEngine<FiscalPeriod>` with `AllowedNextStates`, guards, status history, `RowVersion`.
- **Close** and **reopen** as SENSITIVE ops: mandatory `Reason`, audit-first → outbox, out-of-order guards.
- The **date→period status lookup** (`GET /api/v1/periods/by-date?date=`), the contract the Journal posting guard consumes.
- Reference-read caching of period status (`SDD-INFRA-004`) with invalidation on every state change; DB fall-through when Redis is down.
- `FiscalPeriodClosedEvent` / `FiscalPeriodReopenedEvent` via the transactional outbox.
- **Fulfillment of SDD-FIN-002's `IPostingPeriodGuard` seam** — the new `GatewayPostingPeriodGuard` in `Finance.Journal.API` and the DI swap that activates `POSTING_PERIOD_CLOSED` (§2.7).

**Scope — excluded (deferred):**
- **Country-aligned fiscal-year start / non-calendar periods / 13th adjustment period** — SDD-CTRY-001. v1 assumes calendar-aligned months behind the strategy seam.
- **FX revaluation / period-end valuation at close** — SDD-FIN-005.
- **Year-end close, retained-earnings roll-forward, opening-balance carry** and a standalone `FiscalYear` aggregate — a later batch.
- **Period-close *checklist*** (e.g. "all sub-ledgers reconciled / no unposted drafts before close") — a later batch; v1 enforces only the out-of-order-close ordering guard.
- **Soft re-derivation of period status into other services' caches via pub/sub** — only the close/reopen events are published; consumers (e.g. Journal cache invalidation) subscribe in their own batches.

## 2. Behavior

> **Service/controller contract (SDD-INFRA-009).** `FiscalPeriodService` inherits `SearchableServiceBase<FiscalPeriod, FiscalPeriodDto, PeriodsDbContext>` (and thus `BaseEntityService<PeriodsDbContext>`). Every public service method MUST return `Result` / `Result<T>` — never `null`, never a thrown exception for a business outcome. `FiscalPeriodsController` inherits `BaseApiController` and translates every result via `ToActionResult(...)`. State transitions MUST go through `IWorkflowEngine<FiscalPeriod>` (SDD-INFRA-008); the service owns `SaveChanges` / `RowVersion` / status-history append inside the outbox transaction. Error-code → HTTP-status mapping and the ProblemDetails shape are owned by SDD-INFRA-001.

### 2.1 State machine (MUST — SDD-INFRA-008)
- `FiscalPeriod` MUST be a workflow aggregate with exactly two states and these `AllowedNextStates`:
  - `Open` → { `Closed` }.
  - `Closed` → { `Open` } (reopen).
- Any transition not in `AllowedNextStates` MUST be rejected by the engine with `INVALID_PERIOD_STATE_TRANSITION`. (The engine's generic code is `INVALID_STATE_TRANSITION`, SDD-INFRA-008 §4; the Periods domain surfaces the domain alias `INVALID_PERIOD_STATE_TRANSITION` so responses are self-describing — see §4.)
- A newly generated period MUST be created in `Open` (§2.3).

### 2.2 Generate periods for a fiscal year (MUST)
- `POST /api/v1/periods/generate` MUST generate the set of fiscal periods for a supplied `FiscalYear` in a single call. Requires permission `finance.period:create`.
- v1 MUST generate **12 calendar-aligned monthly periods** (`PeriodNumber` 1–12), each with `StartDate` = first instant of the month and `EndDate` = last instant of the month, all `Open`. The calendar derivation MUST be obtained behind the (future) `ICountryStrategy` fiscal-calendar seam — v1 uses a calendar-month default implementation so SDD-CTRY-001 can substitute a non-calendar start without changing this endpoint (SDD-CTRY-001 deferral).
- Generation MUST be idempotent at the year grain: if any period already exists for the supplied `FiscalYear`, the call MUST fail with `DUPLICATE_PERIOD` and MUST NOT create a partial set.
- The generated periods MUST be **contiguous and non-overlapping**: each period's `StartDate` MUST equal the prior period's `EndDate` + the smallest representable increment (no gaps), and no two periods may overlap (`OVERLAPPING_PERIOD`).
- `(FiscalYear, PeriodNumber)` MUST be unique (enforced by a unique index, §3 / §6).
- Generation MUST write an audit `Create` row per period (or one batch audit row covering the year — see §7) and MUST NOT publish a domain event (only close/reopen publish events).
- Generation MUST invalidate the `finance-periods:*` cache region (§2.8).

### 2.3 Create a single period (MAY)
- `POST /api/v1/periods` MAY create one period explicitly (for manual calendars). Requires `finance.period:create`.
- The same uniqueness, contiguity, and non-overlap rules as §2.2 MUST hold: a period whose `(FiscalYear, PeriodNumber)` already exists MUST yield `DUPLICATE_PERIOD`; a period whose `[StartDate, EndDate]` overlaps an existing period MUST yield `OVERLAPPING_PERIOD`.
- The period MUST be created in `Open` with an audit `Create` row and a cache invalidation.

### 2.4 Close a period (MUST — SENSITIVE op, SDD-AUDIT-001)
- `POST /api/v1/periods/{id}/close` MUST transition an `Open` period to `Closed` via `IWorkflowEngine<FiscalPeriod>`. Requires permission `finance.period:close`.
- A non-empty `Reason` MUST be supplied; a missing reason MUST yield `CLOSE_REASON_REQUIRED` **before** any state change, audit row, or event (close is on SDD-AUDIT-001's mandatory-`Reason` list).
- The period MUST be `Open`; closing an already-`Closed` period MUST yield `PERIOD_ALREADY_CLOSED`.
- **Out-of-order close MUST be rejected:** the period MUST NOT be closed while an *earlier* period (lower `(FiscalYear, PeriodNumber)`) in the same `FiscalYear` is still `Open` — this MUST yield `CANNOT_CLOSE_OUT_OF_ORDER` (a workflow guard, SDD-INFRA-007/-008).
- On a successful transition the service MUST, within a single SaveChanges/outbox transaction and in this order:
  1. Run the §2.4 guards (reason present, state `Open`, ordering).
  2. Stamp `ClosedAt = SYSDATETIMEOFFSET()` and `ClosedBy` from the principal; set `Status = Closed`.
  3. Write an audit `StateChange` row (`EventType = "FiscalPeriodClosed"`, `BeforeJson` = open snapshot, `AfterJson` = closed snapshot, carrying the `Reason`) **before** the outbox row (audit-first, SDD-AUDIT-001 §2.4).
  4. Enqueue `FiscalPeriodClosedEvent` to the outbox (atomic with the DB write — no `await _bus.Publish` outside the outbox, no try/catch, SDD-INFRA-006).
  5. Append the `FiscalPeriodStatusHistory` row (`Open → Closed`, who/when/correlation/reason) and increment `RowVersion` (SDD-INFRA-008 §2.4).
  6. Invalidate the `finance-periods:*` cache region (§2.8) so the next date→status lookup reflects `Closed`.

### 2.5 Reopen a period (MUST — SENSITIVE op, SDD-AUDIT-001)
- `POST /api/v1/periods/{id}/reopen` MUST transition a `Closed` period back to `Open` via the workflow engine. Requires permission `finance.period:reopen`.
- A non-empty `Reason` MUST be supplied; a missing reason MUST yield `REOPEN_REASON_REQUIRED` before any change (reopen is a SENSITIVE op).
- The period MUST be `Closed`; reopening an already-`Open` period MUST yield `PERIOD_ALREADY_OPEN`.
- **Out-of-order reopen MUST be rejected (symmetric to close):** a period MUST NOT be reopened while a *later* period (higher `(FiscalYear, PeriodNumber)`) in the same `FiscalYear` is `Closed` — this MUST yield `CANNOT_CLOSE_OUT_OF_ORDER` (the same ordering-violation code; reopening an earlier period under a closed later period would create an inconsistent calendar). (See §7 — reuses the ordering code rather than minting a separate reopen-ordering code.)
- The transition side effects mirror §2.4: clear `ClosedAt`/`ClosedBy` (or retain them as historical and stamp a `ReopenedAt`/`ReopenedBy` — see §7), set `Status = Open`, write an audit `StateChange` (`EventType = "FiscalPeriodReopened"`, carrying the `Reason`) audit-first, enqueue `FiscalPeriodReopenedEvent` to the outbox, append the status-history row (`Closed → Open`), increment `RowVersion`, and invalidate the cache region.

### 2.6 Date→period status lookup (MUST — the SDD-FIN-002 integration contract)
- `GET /api/v1/periods/by-date?date={date}` MUST return the `FiscalPeriodDto` of the single period whose `[StartDate, EndDate]` contains the supplied date, including its `Status`. Requires permission `finance.period:read`.
- The bounds check MUST be inclusive of `StartDate` and `EndDate` and MUST resolve to exactly one period (the non-overlap invariant of §2.2/§2.3 guarantees uniqueness).
- When **no** period contains the date, the service MUST return `NO_PERIOD_FOR_DATE` (404) — it MUST NOT silently treat the missing period as `Closed` (resolved decision §7: a misconfigured calendar must be visible).
- This read MAY be served from cache (`finance-periods:by-date:{yyyy-MM-dd}` or `finance-periods:status:all`, §2.8). The cached value MUST reflect the latest close/reopen because every state change invalidates the region.
- A companion `GET /api/v1/periods/by-year-number?fiscalYear={y}&periodNumber={n}` MAY return the period by its natural key for callers that already know the period coordinates.

### 2.7 Fulfillment of SDD-FIN-002's `IPostingPeriodGuard` seam (MUST — cross-service)
- This spec MUST replace the Journal service's `AlwaysOpenPostingPeriodGuard` (SDD-FIN-002 §2.7) with a real `GatewayPostingPeriodGuard` that implements `IPostingPeriodGuard.EnsurePostableAsync(DateTimeOffset entryDate, CancellationToken ct)` by calling the Periods service's `GET /api/v1/periods/by-date` **through the Finance Gateway** via a Refit client, following the same cross-service-read convention as Batch 10's `GatewayReferenceDataReader` (Refit → gateway → `CorrelationIdDelegatingHandler` → `ServiceToServiceJwtHandler` → `AddStandardResilienceHandler`).
- The guard MUST resolve as follows:
  - Period found and `Open` → `Result.Success()` (posting allowed).
  - Period found and `Closed` → `Result.Failure(JournalErrorCodes.POSTING_PERIOD_CLOSED)` (posting blocked) — **this activates the dormant Batch-10 rule**.
  - **No period** for `entryDate` (the Periods lookup returns `NO_PERIOD_FOR_DATE` / 404) → `Result.Failure(JournalErrorCodes.POSTING_PERIOD_CLOSED)` — a date with no defined period is not postable. (The Periods service surfaces the distinct `NO_PERIOD_FOR_DATE`; the Journal guard collapses both "closed" and "no period" to the single posting-side `POSTING_PERIOD_CLOSED` rejection so the posting contract is unchanged. See §7.)
  - **Periods service unreachable / any non-404 upstream error** → `Result.Failure(JournalErrorCodes.POSTING_PERIOD_CLOSED)` — **fail closed** (block posting), matching the `GatewayReferenceDataReader` fail-closed convention. Financial safety over availability (resolved decision §7).
- The DI registration of `IPostingPeriodGuard` in `Finance.Journal.API/Program.cs` MUST switch from `AlwaysOpenPostingPeriodGuard` to `GatewayPostingPeriodGuard`; the Journal **posting code is untouched** (the seam is the only change point, per SDD-FIN-002 §2.7). The Journal test suite's existing `Post_ClosedPeriod_ReturnsPostingPeriodClosed_WhenGuardRejects` (SDD-FIN-002 §6.1) becomes reachable through the real guard via a faked Periods reader.
- This fulfillment touches **two services in the same batch**: the new `Finance.Periods.API` AND `Finance.Journal.API` (guard swap + Refit client + handler-chain registration). The implementor MUST NOT add a cross-database join — the Journal service reads period status only through the gateway Refit client (Plan §8 forbids cross-DB joins).

### 2.8 Reference-read caching (MUST — SDD-INFRA-004)
- Period status is **reference data** and MUST be cacheable. The date→status lookup (§2.6) and a full open/closed status map for dropdowns MAY be cached under the bounded `finance-periods:` prefix (e.g. `finance-periods:by-date:{yyyy-MM-dd}`, `finance-periods:status:{fiscalYear}`).
- Every generate, create, close, and reopen MUST invalidate the `finance-periods:*` region (pattern removal bounded to the `finance-periods:` prefix).
- Cache access MUST fall through to the database if Redis is unreachable — service availability (and therefore the Journal posting guard) MUST NOT depend on Redis (SDD-INFRA-004).
- Transactional data is not in scope here; the cached values are status flags only.

### 2.9 Domain events (MUST — SDD-INFRA-006)
- Close MUST publish `FiscalPeriodClosedEvent`; reopen MUST publish `FiscalPeriodReopenedEvent`.
- Events MUST be `sealed record` types implementing `IFinanceEvent` in `src/Finance.ServiceModel/Events/Periods/`, with `required` properties + `MessageId` + `CorrelationId` + `OccurredAt`, plus `FiscalPeriodId`, `FiscalYear`, `PeriodNumber`, `StartDate`, `EndDate`, and `Reason`. Because the surrogate `Id` is INT IDENTITY (not externally stable), consumers MUST key off `(FiscalYear, PeriodNumber)` rather than the surrogate (resolved decision §7).
- `CorrelationId` MUST be sourced from `ICorrelationIdAccessor`; `MessageId` MUST be a new GUID; `OccurredAt` MUST be `DateTimeOffset.UtcNow`.
- Publication MUST go through the MassTransit EF-Core transactional outbox configured on `PeriodsDbContext` (atomic with the DB transaction). The service MUST NOT `await _bus.Publish(...)` outside the outbox and MUST NOT wrap the publish in try/catch (SDD-INFRA-006).

### 2.10 Audit trail (MUST — SDD-AUDIT-001)
- Generate/create MUST record an audit `Create` entry (`BeforeJson = null`).
- Close MUST record an audit `StateChange` entry (`BeforeJson` = open snapshot) with a **non-empty `Reason`**.
- Reopen MUST record an audit `StateChange` entry (`BeforeJson` = closed snapshot) with a **non-empty `Reason`**.
- Audit rows MUST be written in the SAME transaction as the change and BEFORE the outbox publish (audit-first), via the shared `IAuditService.RecordAsync` into the `audit` schema; the service MUST NOT bypass it.

### 2.11 List & get (MUST)
- `GET /api/v1/periods` MUST accept a `FilterRequest` and return `PagedResult<FiscalPeriodDto>` (SDD-INFRA-005), default-ordered by `FiscalYear` descending then `PeriodNumber` ascending (the library always appends the PK as the final deterministic sort term). `PageSize` capped at 200. Requires `finance.period:read`.
  - Filterable/sortable surface MUST be opt-in via `[Filterable]`/`[Sortable]` on `FiscalPeriod`: `FiscalYear`, `PeriodNumber`, `Status`, `StartDate`, `EndDate`.
  - The filtered/paged list MUST NOT be cached (only the reference status lookups of §2.8 are cached).
- `GET /api/v1/periods/{id}` MUST return the period or `PERIOD_NOT_FOUND` (404). Requires `finance.period:read`.

### 2.12 Optimistic concurrency (MUST)
- `FiscalPeriod` MUST carry a `RowVersion` (`rowversion` / `byte[]`) concurrency token configured via Fluent API; `FiscalPeriodDto` MUST expose it as a base64 string.
- A concurrent close/reopen detected by `SaveWithConcurrencyCheck` (SDD-INFRA-009) MUST surface as `CONCURRENT_MODIFICATION` (409).

### 2.13 Cross-cutting obligations (MUST)
- Every endpoint MUST be protected by `[RequirePermission("finance.period:<action>")]` decoded via the shared `Warehouse.Auth.Shared` package (SDD-INT-AUTH-001), and `ValidateFinanceJwtConfiguration` MUST be called at startup (SDD-INT-AUTH-001).
- `CorrelationId` MUST flow via `ICorrelationIdAccessor`/`CorrelationIdMiddleware` and be stamped onto every published event (SDD-INFRA-001/006).
- The service MUST be traced via OpenTelemetry with the `correlation_id` Activity tag (SDD-OBS-001); logging MUST use NLog structured templates (no string interpolation). The service MUST expose `/health/live` and `/health/ready`.
- The Gateway MUST gain a periods cluster + routes (`/api/v1/periods` → 6002) and include the periods `/health/ready` in its derived aggregation (SDD-INFRA-002).

### 2.14 Edge cases (MUST)
- **Close an already-closed period.** `POST .../{id}/close` on a `Closed` period MUST return `PERIOD_ALREADY_CLOSED` — never a second event, never a second audit row.
- **Reopen an already-open period.** `POST .../{id}/reopen` on an `Open` period MUST return `PERIOD_ALREADY_OPEN`.
- **Close out of order.** Closing period N while period N-1 in the same year is `Open` MUST return `CANNOT_CLOSE_OUT_OF_ORDER` before any state change.
- **Close/reopen without a reason.** MUST return `CLOSE_REASON_REQUIRED` / `REOPEN_REASON_REQUIRED` before any audit row or event.
- **Lookup a date with no period.** `GET /api/v1/periods/by-date` for an uncovered date MUST return `NO_PERIOD_FOR_DATE` (404), never an implicit "closed".
- **Generate a year that already has periods.** MUST return `DUPLICATE_PERIOD` and create nothing (no partial set).
- **Generate overlapping ranges / create an overlapping period.** MUST return `OVERLAPPING_PERIOD`.
- **Concurrent close of the same period.** Two simultaneous closes — one MUST win; the other MUST fail with `CONCURRENT_MODIFICATION` (RowVersion mismatch).
- **Posting into a closed period (cross-service, via §2.7).** The Journal `Draft → Posted` path MUST return `POSTING_PERIOD_CLOSED` when the Periods lookup reports `Closed`, `NO_PERIOD_FOR_DATE`, or is unreachable (fail-closed).

## 3. Validation Rules

### 3.1 Field-level (FluentValidation — codes in `PeriodErrorCodes`)

| Request | Field | Rule | Error code |
|---|---|---|---|
| Generate | `FiscalYear` | Required; plausible range (e.g. 2000–2100) | `INVALID_PERIOD` |
| Create | `FiscalYear` | Required | `INVALID_PERIOD` |
| Create | `PeriodNumber` | Required; 1–12 (v1 calendar months) | `INVALID_PERIOD` |
| Create | `StartDate` / `EndDate` | Required; `StartDate` < `EndDate` | `INVALID_PERIOD` |
| Close | `Reason` | NotEmpty | `CLOSE_REASON_REQUIRED` |
| Reopen | `Reason` | NotEmpty | `REOPEN_REASON_REQUIRED` |
| By-date lookup | `date` | Required, parseable `DateTimeOffset` | `INVALID_PERIOD` |

### 3.2 Cross-aggregate / workflow guards (SDD-INFRA-007 / SDD-INFRA-008)

| Transition / op | Guard | Error code |
|---|---|---|
| Generate / Create | `(FiscalYear, PeriodNumber)` unique | `DUPLICATE_PERIOD` |
| Generate / Create | `[StartDate, EndDate]` does not overlap an existing period | `OVERLAPPING_PERIOD` |
| Open → Closed | No earlier `Open` period exists in the same `FiscalYear` | `CANNOT_CLOSE_OUT_OF_ORDER` |
| Closed → Open | No later `Closed` period exists in the same `FiscalYear` | `CANNOT_CLOSE_OUT_OF_ORDER` |
| any illegal transition | `IWorkflowEngine<FiscalPeriod>` `AllowedNextStates` | `INVALID_PERIOD_STATE_TRANSITION` |

### 3.3 State-based

| Condition | Rule | Error code |
|---|---|---|
| Close a `Closed` period | Reject | `PERIOD_ALREADY_CLOSED` |
| Reopen an `Open` period | Reject | `PERIOD_ALREADY_OPEN` |
| Close/reopen with stale `RowVersion` | Reject | `CONCURRENT_MODIFICATION` |
| Period id not found (get/close/reopen) | Reject | `PERIOD_NOT_FOUND` |
| No period contains the supplied date (by-date lookup) | Reject | `NO_PERIOD_FOR_DATE` |

## 4. Error Rules

All errors are RFC-7807 ProblemDetails per SDD-INFRA-001 (`title` = code in SCREAMING_SNAKE_CASE, `detail` = developer English, `type` = `https://finance.local/errors/{code}`). `BaseApiController.ToActionResult` maps codes to HTTP via `IErrorCodeToStatusMap` (SDD-INFRA-009); services return `Result.Failure(code, detail)`.

| Code | HTTP | Trigger | Type |
|---|---|---|---|
| `PERIOD_NOT_FOUND` | 404 | Period id does not exist (get / close / reopen) | Not found |
| `NO_PERIOD_FOR_DATE` | 404 | No period's `[StartDate, EndDate]` contains the supplied date | Not found (calendar gap) |
| `PERIOD_ALREADY_CLOSED` | 409 | Close attempted on a `Closed` period | Conflict (state) |
| `PERIOD_ALREADY_OPEN` | 409 | Reopen attempted on an `Open` period | Conflict (state) |
| `INVALID_PERIOD_STATE_TRANSITION` | 409 | Transition not in `AllowedNextStates` | Conflict (workflow) |
| `CANNOT_CLOSE_OUT_OF_ORDER` | 409 | Close with an earlier `Open` period (or reopen with a later `Closed` period) in the year | Conflict (ordering) |
| `OVERLAPPING_PERIOD` | 409 | Generated/created range overlaps an existing period | Conflict (calendar) |
| `DUPLICATE_PERIOD` | 409 | `(FiscalYear, PeriodNumber)` already exists | Conflict (uniqueness) |
| `CLOSE_REASON_REQUIRED` | 400 | Close called without a non-empty `Reason` | Validation |
| `REOPEN_REASON_REQUIRED` | 400 | Reopen called without a non-empty `Reason` | Validation |
| `INVALID_PERIOD` | 400 | Generate/create/lookup shape invalid (year/number/dates/date param) | Validation (shape) |
| `CONCURRENT_MODIFICATION` | 409 | Stale `RowVersion` on close/reopen | Conflict (concurrency) |

`PERIOD_ALREADY_CLOSED`, `PERIOD_ALREADY_OPEN`, `INVALID_PERIOD_STATE_TRANSITION`, `CANNOT_CLOSE_OUT_OF_ORDER`, `OVERLAPPING_PERIOD`, and `DUPLICATE_PERIOD` are state/uniqueness conflicts → **409**; the `DefaultErrorCodeToStatusMap` MUST be extended to map these (none match the default `*_NOT_FOUND`/`*_CONFLICT`/`CONCURRENT_*` patterns). `PERIOD_NOT_FOUND` and `NO_PERIOD_FOR_DATE` → **404**. `CLOSE_REASON_REQUIRED`, `REOPEN_REASON_REQUIRED`, `INVALID_PERIOD` → **400**.

`INVALID_PERIOD_STATE_TRANSITION` is the Periods-domain alias surfaced to clients for the workflow engine's generic `INVALID_STATE_TRANSITION` (SDD-INFRA-008 §4); the service translates the engine's failure code to the domain code so responses are self-describing.

**Cross-service note (SDD-FIN-002).** When the Journal posting guard (§2.7) consumes this service's `NO_PERIOD_FOR_DATE`/`Closed`/unreachable results, the **posting-side** response is the Journal domain's `POSTING_PERIOD_CLOSED` (409, defined in `JournalErrorCodes` since Batch 10). The Periods service's own codes are not surfaced to the Journal caller — the guard collapses them to the single posting rejection (§2.7, §7).

Constants live in `Finance.Common.ErrorCodes.PeriodErrorCodes`: `PERIOD_NOT_FOUND`, `NO_PERIOD_FOR_DATE`, `PERIOD_ALREADY_CLOSED`, `PERIOD_ALREADY_OPEN`, `INVALID_PERIOD_STATE_TRANSITION`, `CANNOT_CLOSE_OUT_OF_ORDER`, `OVERLAPPING_PERIOD`, `DUPLICATE_PERIOD`, `CLOSE_REASON_REQUIRED`, `REOPEN_REASON_REQUIRED`, `INVALID_PERIOD`. `CONCURRENT_MODIFICATION` is referenced from `CommonErrorCodes` (single source, SDD-INFRA-008/009) — NOT redefined. `POSTING_PERIOD_CLOSED` already lives in `JournalErrorCodes` (SDD-FIN-002) and is NOT redefined here.

**Frontend obligation (no frontend in this batch).** Every code above MUST get a matching `errors.<CODE>` entry in BOTH `frontend/src/shared/i18n/locales/en.ts` and `bg.ts` in the same PR as the Periods frontend (SDD-UI-001). Backend-only this batch; recorded for the frontend phase.

## 5. Versioning Notes

`/api/v1/periods/*` is the v1 surface: `POST /generate`, `POST` (create one), `POST /{id}/close`, `POST /{id}/reopen`, `GET` (list), `GET /{id}`, `GET /by-date`, `GET /by-year-number`.

- **v1 — Initial specification (Batch 11).** `FiscalPeriod` core model + `FiscalPeriodStatusHistory`; per-year calendar-month generation; `Open ⇄ Closed` lifecycle via `IWorkflowEngine<FiscalPeriod>`; close/reopen as SENSITIVE ops (mandatory `Reason`, audit-first → outbox `FiscalPeriodClosedEvent`/`FiscalPeriodReopenedEvent`); out-of-order-close ordering guard; non-overlap + per-year uniqueness; date→status lookup; reference-status caching with invalidation; **fulfillment of SDD-FIN-002's `IPostingPeriodGuard` seam** via `GatewayPostingPeriodGuard` (activating `POSTING_PERIOD_CLOSED`, fail-closed).
- **SDD-FIN-002 activation note.** This spec activates the previously-dormant `POSTING_PERIOD_CLOSED` rule from SDD-FIN-002 §2.7. It is **purely additive** to SDD-FIN-002 (the seam, the error code, and the test stub already existed in Batch 10) — no Journal API version bump and no Journal contract change; only the DI registration of `IPostingPeriodGuard` changes.
- **Deferred (future versions / specs):**
  - **Country-aligned fiscal-year start / non-calendar periods / 13th adjustment period** — SDD-CTRY-001; the generation path is written behind the `ICountryStrategy` fiscal-calendar seam so this is additive.
  - **FX revaluation / period-end valuation at close** — SDD-FIN-005; adds a close-time step, additive.
  - **Year-end close / retained-earnings roll-forward / standalone `FiscalYear` aggregate** — a later batch; introducing a `FiscalYear` aggregate with its own lifecycle is a structural change requiring a `CHG-ENH-*` and a migration.
  - **Period-close checklist** (sub-ledger reconciliation / no-unposted-drafts gates before close) — a later batch; adds close-time guards, additive.
- Adding an event field is additive; changing the state set (e.g. introducing a hard `Locked` state) or the transition semantics is breaking and requires `/api/v2/` + a `CHG-ENH-*` + an enum migration.

## 6. Test Plan

> Environment: Docker/SQL/Redis/RabbitMQ are not available — only `[Unit]` tests run by default. EF unit tests use SQLite in-memory; the workflow engine, guards, generation, and date-lookup logic are testable without a real broker (the outbox publish is asserted via the MassTransit in-memory test harness). `WebApplicationFactory` HTTP tests + the cross-service `GatewayPostingPeriodGuard`-through-real-gateway tests carry `[Category("Integration")]` and are excluded from the default run. All business tests MUST reference `[Category("SDD-FIN-004")]`; the Journal-side guard tests additionally reference `[Category("SDD-FIN-002")]`.

### 6.1 State machine & lifecycle (Unit)

| Test name | Kind |
|---|---|
| `Close_OpenPeriod_TransitionsToClosed` | [Unit] |
| `Close_AlreadyClosedPeriod_ReturnsPeriodAlreadyClosed` | [Unit] |
| `Close_WithoutReason_ReturnsCloseReasonRequired_NoStateChange` | [Unit] |
| `Close_WhileEarlierPeriodOpen_ReturnsCannotCloseOutOfOrder` | [Unit] |
| `Reopen_ClosedPeriod_TransitionsToOpen` | [Unit] |
| `Reopen_AlreadyOpenPeriod_ReturnsPeriodAlreadyOpen` | [Unit] |
| `Reopen_WithoutReason_ReturnsReopenReasonRequired_NoStateChange` | [Unit] |
| `Reopen_WhileLaterPeriodClosed_ReturnsCannotCloseOutOfOrder` | [Unit] |
| `Workflow_OpenAllowsOnlyClosed_ClosedAllowsOnlyOpen` | [Unit] |
| `Close_StaleRowVersion_ReturnsConcurrentModification` | [Unit] |

### 6.2 Close/reopen side effects (Unit — SQLite in-memory + MassTransit test harness)

| Test name | Kind |
|---|---|
| `Close_StampsClosedAtAndClosedBy` | [Unit] |
| `Close_RecordsAuditStateChange_WithReason_BeforeOutboxPublish` | [Unit] |
| `Close_PublishesFiscalPeriodClosedEvent_WithFiscalYearPeriodNumberAndCorrelationId` | [Unit] |
| `Close_AppendsStatusHistoryRow_OpenToClosed` | [Unit] |
| `Close_InvalidatesFinancePeriodsCacheRegion` | [Unit] |
| `Reopen_RecordsAuditStateChange_WithReason_BeforeOutboxPublish` | [Unit] |
| `Reopen_PublishesFiscalPeriodReopenedEvent_WithReason` | [Unit] |
| `Reopen_AppendsStatusHistoryRow_ClosedToOpen` | [Unit] |
| `Close_DoesNotPublishEvent_WhenGuardFails` | [Unit] |

### 6.3 Generation, lookup & validation (Unit)

| Test name | Kind |
|---|---|
| `Generate_TwelveCalendarMonths_AllOpen_ContiguousAndNonOverlapping` | [Unit] |
| `Generate_YearWithExistingPeriods_ReturnsDuplicatePeriod_CreatesNothing` | [Unit] |
| `Create_OverlappingRange_ReturnsOverlappingPeriod` | [Unit] |
| `Create_DuplicateYearAndNumber_ReturnsDuplicatePeriod` | [Unit] |
| `ByDate_ReturnsContainingPeriod_WithStatus` | [Unit] |
| `ByDate_NoCoveringPeriod_ReturnsNoPeriodForDate` | [Unit] |
| `ByDate_BoundaryDate_IsInclusiveOfStartAndEnd` | [Unit] |
| `Get_ReturnsNotFound_WhenPeriodDoesNotExist` | [Unit] |
| `Search_ReturnsPagedResultOrderedByFiscalYearDescThenPeriodNumberAsc` | [Unit] |
| `Search_CapsPageSizeAt200` | [Unit] |
| `Search_DoesNotCacheFilteredList` | [Unit] |
| `CloseRequestValidator_RejectsEmptyReason` | [Unit] |
| `ReopenRequestValidator_RejectsEmptyReason` | [Unit] |
| `FiscalPeriodConfiguration_HasUniqueIndexOnFiscalYearAndPeriodNumber` | [Unit] |
| `FiscalPeriodConfiguration_ConfiguresRowVersionConcurrencyToken` | [Unit] |
| `PeriodErrorCodes_DefinesAllPeriodCodes` | [Unit] |

### 6.4 SDD-FIN-002 guard fulfillment (Unit — Journal service, faked Periods reader)

| Test name | Kind |
|---|---|
| `GatewayPostingPeriodGuard_OpenPeriod_ReturnsSuccess` | [Unit] |
| `GatewayPostingPeriodGuard_ClosedPeriod_ReturnsPostingPeriodClosed` | [Unit] |
| `GatewayPostingPeriodGuard_NoPeriodForDate_ReturnsPostingPeriodClosed` | [Unit] |
| `GatewayPostingPeriodGuard_PeriodsServiceUnreachable_FailsClosed_ReturnsPostingPeriodClosed` | [Unit] |
| `Post_IntoClosedPeriod_ReturnsPostingPeriodClosed_ViaRealGuard` | [Unit] |

### 6.5 Endpoint, wiring & cross-service (Integration — `[Category("Integration")]`, excluded from default run)

| Test name | Kind |
|---|---|
| `Generate_Returns201_AndPersistsTwelveOpenPeriods` | [Integration] |
| `Close_Returns200_AndWritesOutboxAndAuditRow_InSameTransaction` | [Integration] |
| `Close_Returns400_WhenReasonMissing` | [Integration] |
| `Close_Returns409_WhenAlreadyClosed` | [Integration] |
| `Close_Returns409_WhenOutOfOrder` | [Integration] |
| `Reopen_Returns200_AndFlipsClosedToOpen` | [Integration] |
| `ByDate_Returns200_WithContainingPeriod` | [Integration] |
| `ByDate_Returns404_WhenNoPeriodForDate` | [Integration] |
| `Close_ConcurrentCallers_OneFailsWithConcurrentModification` | [Integration] |
| `Endpoint_Returns403_WhenPermissionMissing` | [Integration] |
| `JournalPost_Returns409PostingPeriodClosed_WhenPeriodsServiceReportsClosed` | [Integration] |
| `JournalPost_Returns409PostingPeriodClosed_WhenPeriodsServiceUnreachable_FailClosed` | [Integration] |

## 7. Resolved Decisions & Open Items

### Resolved
- **`FiscalYear` is a column, not an aggregate (v1).** Periods carry `FiscalYear` (int) + `PeriodNumber` (int) as their natural key; generation is per-year. A standalone `FiscalYear` aggregate with its own year-open/close lifecycle and retained-earnings roll-forward is deferred to a later batch — it would be a structural change (new aggregate + migration), not needed to fulfill the SDD-FIN-002 seam.
- **Two states (`Open`/`Closed`); reopen is `Closed → Open`.** No distinct `Reopened` state — the audit `StateChange` (`EventType = "FiscalPeriodReopened"`) and `FiscalPeriodReopenedEvent` record that an open was a reopen. A hard `Locked`/permanently-closed state is deferred (would be a breaking enum change, §5).
- **Calendar-aligned monthly periods (1–12) in v1**, derived behind the future `ICountryStrategy` fiscal-calendar seam. Non-calendar start + 13th/adjustment period → SDD-CTRY-001.
- **Out-of-order close is a MUST (rejected), not a SHOULD.** Closing N while N-1 is open, or reopening N while N+1 is closed, both yield `CANNOT_CLOSE_OUT_OF_ORDER`. Reusing one ordering code (rather than a separate reopen-order code) keeps the contract small; the `detail` text distinguishes the direction.
- **No-period-for-a-date is a distinct hard failure (`NO_PERIOD_FOR_DATE`, 404)** — never silently "closed" — so a misconfigured calendar surfaces visibly. The Journal guard, however, collapses "no period" + "closed" + "unreachable" into the single posting-side `POSTING_PERIOD_CLOSED` so the SDD-FIN-002 posting contract is unchanged.
- **The Journal `GatewayPostingPeriodGuard` fails closed.** Unreachable Periods service ⇒ block posting (return `POSTING_PERIOD_CLOSED`), matching the Batch-10 `GatewayReferenceDataReader` convention. Financial safety over availability; the cache fall-through (§2.8) keeps the Periods service itself available when only Redis is down.
- **Events key off `(FiscalYear, PeriodNumber)`, not the surrogate.** Because `FiscalPeriod.Id` is INT IDENTITY (internal-only, Plan §509), the published events carry the natural key for stable cross-service correlation; no external GUID is needed (no NEWSEQUENTIALID requirement).
- **Service identity.** `Finance.Periods.API`, **port 6002**, database `finance_periods`, schema `periods` (Plan §5/§8/§9). (The Batch-11 brief's "6005/6006/6007" guess is superseded by the authoritative plan — 6005 belongs to Invoices.)

### Open / deferred (for the Phase-2 implementor)
- **Generation audit granularity.** §2.2 allows either one audit `Create` row per generated period or a single batch row for the year. Recommend per-period rows for completeness (matches SDD-ACCT-001 create-audit), but a single batch row is acceptable if 12 rows per generate is judged noisy — decide once and document in the change spec.
- **Reopen timestamp retention.** §2.5 leaves open whether reopen clears `ClosedAt`/`ClosedBy` or retains them and stamps a separate `ReopenedAt`/`ReopenedBy`. Recommend retaining the prior close stamp as historical and adding `ReopenedAt`/`ReopenedBy` (or relying solely on `FiscalPeriodStatusHistory`) so the close record is not lost. The status-history table is the durable record either way.
- **Journal-side wiring (the seam swap — MUST happen in this batch).** In `Finance.Journal.API`: (1) add a Refit `IPeriodReadClient` (`GET /api/v1/periods/by-date`) registered through the gateway with the standard handler chain (`CorrelationIdDelegatingHandler` → `ServiceToServiceJwtHandler` → `AddStandardResilienceHandler`), mirroring `GatewayReferenceDataReader` / `IReferenceDataReader`; (2) add `GatewayPostingPeriodGuard : IPostingPeriodGuard` (`src/Interfaces/Journal/Finance.Journal.API/Workflow/`) implementing the §2.7 resolution table (fail-closed); (3) change the DI registration in `Finance.Journal.API/Program.cs` from `AlwaysOpenPostingPeriodGuard` to `GatewayPostingPeriodGuard`; (4) add a fake period reader to `Finance.Journal.API.Tests/Fixtures/` (alongside `FakeReferenceDataReader`) so §6.4 unit tests run offline. The Journal **posting code (`JournalEntryService`, workflow states/guards) is NOT changed** — only the seam implementation and its registration.
- **Cross-service cache reaction (deferred).** `FiscalPeriodClosedEvent`/`ReopenedEvent` are published so other services (e.g. a future Journal-side period-status cache) can react; no consumer is wired in this batch beyond the existing EventLog archival pattern (which MAY add the two events in its own batch).
- **Country fiscal-calendar seam.** The calendar-month generator MUST be isolated behind an interface so SDD-CTRY-001 can replace it; do not hard-code month boundaries into the service method.
