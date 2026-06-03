# SDD-FIN-003 — General Ledger & Trial Balance

> Status: Implemented (Batch 13 — read-only GL aggregation + trial balance over posted-to-ledger journal lines, in the existing `Finance.Journal.API`. No new tables, events, audit writes, or workflow. Backend + 107 green `[Unit]` tests shipped; validated spec↔code↔tests. Account normal-balance-side presentation + FX revaluation + materialized reporting views deferred — see §7.)
> Owner: Finance
> Last updated: 2026-06-03
> Category: Core
> Service: `Finance.Journal.API` — port **6004**, database `finance_journal`
> Related: SDD-FIN-001 (Double-Entry Engine — the `JournalEntry`/`JournalEntryLine` entities + the balance-in-base-currency invariant this spec aggregates over), SDD-FIN-002 (Journal Entry lifecycle — defines `Posted`/`Reversed`/`Draft`; lines posted to the ledger are aggregated — owning status `Posted` ∪ `Reversed` — and a `Reversed` original keeping its lines while its sign-flipped reversal entry is `Posted` is why reversals net to zero here), SDD-ACCT-001 (accounts are the GL aggregation key; code/name enrichment), SDD-INFRA-005 (filtering/paging — the account-ledger line list uses `FilterRequest`→`PagedResult`), SDD-INFRA-009 (base service/controller, `Result<T>`, `SearchableServiceBase`), SDD-INFRA-001 (correlation, ProblemDetails, decimal arithmetic), SDD-INFRA-004 (caching rules — GL balances are derived from transactional data and MUST NOT be cached), SDD-INT-AUTH-001 (RBAC — `finance.journal:read`), SDD-FIN-005 (Multi-Currency Engine — base-currency amounts only here; FX revaluation/account normal-balance presentation deferred), SDD-OBS-001 (tracing, structured logging), SDD-RPT-001 (Reporting — Trial Balance — a future formatted/exported reporting view that consumes this engine's raw aggregation; this spec is the in-engine read primitive, not the report rendering)
> ISA-95: Level 4 (Business Planning & Logistics) — Reporting

---

## 1. Context & Scope

The General Ledger (GL) and Trial Balance are the **read-only roll-up** over the journal. SDD-FIN-001 defines the `JournalEntry`/`JournalEntryLine` entities and the non-negotiable double-entry invariant (Σ base-currency debits == Σ base-currency credits per balanced entry); SDD-FIN-002 defines the `Draft → Posted → Reversed` lifecycle that decides which lines are "in force". This spec defines the two query primitives that aggregate the **`Posted`** lines into account balances:

1. **Trial Balance** — for an as-of date (and/or a from/to range), every account that has any posted activity gets a `TotalDebit`, a `TotalCredit`, and a net (`TotalDebit − TotalCredit`) placed in the debit column when ≥ 0 and the credit column otherwise. The headline invariant — **Σ TotalDebit == Σ TotalCredit across all accounts** — follows directly from SDD-FIN-001 (every posted entry is balanced in base currency, so the union of all posted entries is balanced too) and MUST be asserted by the response (`balanced` flag + grand totals).
2. **GL account ledger** — for one account over a date range: an opening balance (the net of all posted base debits − credits **before** `fromDate`), the in-range posted lines (entry number, date, description, debit, credit, running balance), and a closing balance. This is the drill-down behind a single trial-balance row.

This is a **read-only aggregation**. It owns **no new tables**, publishes **no events**, writes **no audit rows**, and runs **no workflow** — it is a `SELECT … GROUP BY` over the `finance_journal` tables that SDD-FIN-001/-002 already own. All arithmetic is in **base currency**, `decimal` in C# / `DECIMAL(18,2)` in SQL, never `double`/`float` (SDD-FIN-005 / SDD-INFRA-001 / CLAUDE.md §0.3).

**Reversal handling (no special-casing).** A `Reversed` original keeps its `Posted` lines in the ledger AND its sign-flipped reversal entry is itself `Posted` (SDD-FIN-002 §2.6). Both sets of lines are `Posted`, so they are both aggregated and **net to zero naturally**. This spec MUST NOT exclude or special-case `Reversed` originals or reversal entries — the netting is a property of the data, and asserting it is a headline test. `Draft` entries (NULL `EntryNumber`, never posted) are **excluded** from every balance.

**Account enrichment.** Account code / name (and account type when cheaply available) are resolved through the **existing** `IReferenceDataReader` seam already in the Journal service (`GatewayReferenceDataReader` reads the Accounts service through the gateway — SDD-FIN-001 §2.6, resolved §7). This spec MUST reuse that seam and MUST NOT introduce a new cross-service mechanism and MUST NOT cross-database-join into `finance_accounts` (Plan §8). The aggregation itself is account-type-agnostic: it groups by `AccountId` and sums base amounts; the debit/credit column is derived purely from the net sign, NOT from an account "normal balance side" classification (deferred — see §7).

**ISA-95 classification.** The GL and Trial Balance are ISA-95 **Level 4 (Business Planning & Logistics)** financial **reporting** views (ISA-95 / IEC 62264 Part 1, §5 — Business Planning & Logistics). They are read-only projections over the Level-4 bookkeeping business transactions (`JournalEntry`, SDD-FIN-001/-002); they create no new business-transaction records and change no state. Because there is **no state change**, no immutable domain event is required (the immutable-event obligation in SDD-INFRA-006 / SDD-AUDIT-001 applies to state-changing operations — posting and reversal — owned by SDD-FIN-002, not to read queries). No Level-3 (MES) production activity is modelled. Reference data consumed (accounts) is Level-4 master data (SDD-ACCT-001).

**Scope — covered:**
- `GET /api/v1/trial-balance` — as-of (and optional from/to) trial balance grouped by `AccountId`, summing `BaseDebitAmount` / `BaseCreditAmount` of `Posted` lines, with per-account net column placement, a `balanced` flag, and grand totals.
- `GET /api/v1/general-ledger/accounts/{accountId}` — single-account ledger: opening balance, in-range posted lines (filtered/paged via SDD-INFRA-005), running balance, closing balance.
- Aggregation rule: include lines posted to the ledger — entries whose status is `Posted` or `Reversed` (`Draft` excluded). A reversed original (status `Reversed`) keeps its lines on the books and its sign-flipped reversal entry (status `Posted`) offsets it, so the two net naturally with no special-casing.
- Base-currency-only arithmetic (`Base*Amount` columns), `DECIMAL(18,2)`, deterministic ordering.
- Account code/name enrichment via the existing `IReferenceDataReader` seam.
- The Σdebit==Σcredit balanced invariant as a stated MUST and a response field.

**Scope — excluded (deferred):**
- **Account normal-balance side** (Asset/Expense = debit-normal, Liability/Equity/Income = credit-normal) presentation — v1 derives the column purely from the net sign. Presenting balances on their natural normal side is a future enhancement (requires the account-type classification from SDD-ACCT-001; see §7).
- **Formatted / exported reporting** — Balance Sheet, Income Statement, VAT journals, period-end statement layouts, НАП exports (SDD-RPT-001/-002/-003, SDD-INT-NAP-001, SDD-CTRY-001). This spec is the raw in-engine aggregation primitive those reports read; it does not render or export.
- **FX revaluation / multi-currency presentation** — balances are reported in base currency only; transactional-currency breakdowns and period-end FX revaluation are SDD-FIN-005.
- **Caching of GL balances** — explicitly forbidden (SDD-INFRA-004); GL balances are derived from transactional data.
- **Materialized balance tables / running-balance snapshots** — v1 aggregates live on read; a future performance enhancement (a maintained balance table fed by posting events) is a `CHG-ENH-*`, not this spec.
- **Analytic dimensions** beyond the account reference (cost centres, projects) — future.
- **Posting into / locking by fiscal period** — read queries are period-status-agnostic (a `Closed` period's posted lines are still aggregated); period lifecycle is SDD-FIN-004.

## 2. Behavior

> **Service/controller contract (SDD-INFRA-009).** The GL/Trial-Balance service MUST return `Result` / `Result<T>` — never `null`, never a thrown exception for a business outcome. The account-ledger list endpoint MAY inherit `SearchableServiceBase<JournalEntryLine, …, JournalDbContext>` for the SDD-INFRA-005 `FilterRequest`→`PagedResult` mechanics, or compose the filter pipeline directly; either way the read path MUST honor the PageSize cap (200) and deterministic ordering. The controller inherits `BaseApiController` and translates results via `ToActionResult(...)`. Error-code → HTTP-status mapping and the ProblemDetails shape are owned by SDD-INFRA-001 / SDD-INFRA-009.

### 2.1 Aggregation source & inclusion rule (MUST)
- The aggregation MUST read `JournalEntryLine` rows whose owning `JournalEntry.Status` is `Posted` **or** `Reversed` — i.e. every line that has been posted to the ledger. `Draft` entries MUST be excluded from every balance, opening balance, in-range line list, and the trial balance.
- A `Reversed` original entry's lines stay on the books even though the entry's *status* is now `Reversed` (SDD-FIN-002 §2.6 does NOT mutate or remove the original's lines); they MUST be included. The corresponding reversal entry is itself `Posted` and its sign-flipped lines MUST also be included. The implementation MUST NOT filter out `Reversed` originals or detect/special-case reversal entries — they net to zero by construction because the reversal is sign-flipped (SDD-FIN-002 §2.6). This is why the inclusion predicate is `Status ∈ { Posted, Reversed }` and not the narrower `Status == Posted`: the narrower predicate would drop the `Reversed` original and leave only the reversal's offsetting lines, making the account show the negative of the original instead of zero.
- All sums MUST use the **base-currency** columns `BaseDebitAmount` / `BaseCreditAmount` (`DECIMAL(18,2)`), never the transactional `DebitAmount` / `CreditAmount`. The transactional columns MAY be surfaced on the per-line ledger view for display, but balances and the balanced invariant MUST be computed in base currency.
- All monetary results MUST be `decimal` / `DECIMAL(18,2)`; `double`/`float` MUST NOT appear anywhere in the computation (SDD-FIN-005 / SDD-INFRA-001).

### 2.2 Trial Balance — `GET /api/v1/trial-balance` (MUST)
- The endpoint MUST accept an `asOfDate` (the inclusive upper bound of the accounting `EntryDate`) and an optional `fromDate` (the inclusive lower bound). When `fromDate` is omitted, the trial balance is cumulative from the beginning of time up to and including `asOfDate`.
- For each `AccountId` that has at least one `Posted` line within the date window, the response MUST carry:
  - `AccountId`, plus the enriched `AccountCode` / `AccountName` (and account type when available) via the existing `IReferenceDataReader` seam (§2.5).
  - `TotalDebit` = Σ `BaseDebitAmount` of the account's in-window posted lines.
  - `TotalCredit` = Σ `BaseCreditAmount` of the account's in-window posted lines.
  - A net column placement: let `net = TotalDebit − TotalCredit`; the account's `DebitBalance` MUST be `net` when `net ≥ 0` and `0.00` otherwise, and `CreditBalance` MUST be `−net` when `net < 0` and `0.00` otherwise. (An account whose debits and credits are equal — net `0.00` — appears with `DebitBalance == CreditBalance == 0.00`; whether such zero-net accounts are listed is a presentation choice — see §7.)
- The response MUST include grand totals: `GrandTotalDebit` = Σ of every account's `DebitBalance`, `GrandTotalCredit` = Σ of every account's `CreditBalance`.
- The response MUST include a `Balanced` boolean: `Balanced == (GrandTotalDebit == GrandTotalCredit)` compared to the **cent** (`DECIMAL(18,2)`). Because every posted entry is balanced in base currency (SDD-FIN-001 §2.3), a correct aggregation over a consistent ledger MUST always be `Balanced == true`; a `false` value indicates ledger corruption or an aggregation defect and MUST be surfaced (not silently corrected).
- Rows MUST be ordered deterministically by `AccountCode` ascending (falling back to `AccountId` when a code is unavailable) so repeated calls are stable.
- The trial balance MUST NOT be cached (§2.6).
- Requires permission `finance.journal:read` (§2.7).

### 2.3 GL account ledger — `GET /api/v1/general-ledger/accounts/{accountId}` (MUST)
- The endpoint MUST accept `accountId` (route), `fromDate` and `toDate` (query, the inclusive accounting-date window), and a `FilterRequest` (query) for the in-range line list (SDD-INFRA-005).
- The response MUST carry:
  - `AccountId` + enriched `AccountCode` / `AccountName` (§2.5).
  - `OpeningBalance` = Σ (`BaseDebitAmount` − `BaseCreditAmount`) of the account's `Posted` lines whose entry `EntryDate` is strictly **before** `fromDate`. When `fromDate` is omitted, `OpeningBalance` MUST be `0.00`.
  - A paged list of the account's in-window `Posted` lines (`EntryDate` within `[fromDate, toDate]` inclusive), each carrying: `EntryNumber`, `EntryDate`, entry `Description` (and/or line `Description`), `Debit` (base), `Credit` (base), and a `RunningBalance` = `OpeningBalance` + the cumulative (`Debit` − `Credit`) up to and including that line, in ledger order.
  - `ClosingBalance` = `OpeningBalance` + Σ (`BaseDebitAmount` − `BaseCreditAmount`) of all in-window lines (i.e., the running balance after the last in-window line).
- The line list MUST run through `IQueryable<JournalEntryLine>.ApplyFilter(request)` (SDD-INFRA-005), MUST cap `PageSize` at 200, and MUST be ordered by `EntryDate` ascending then `Id` (the PK is always appended as the final deterministic sort term so pagination is stable). Ascending order is REQUIRED so the running balance reads top-to-bottom chronologically.
- An account with **no** posted lines in or before the window MUST return a well-formed ledger with `OpeningBalance == 0.00`, an empty line page, and `ClosingBalance == 0.00` — NOT an error (§2.4).
- The account ledger MUST NOT be cached (§2.6).
- Requires permission `finance.journal:read` (§2.7).

### 2.4 Empty / unknown account handling (MUST)
- An account that exists but has no posted activity in the requested window MUST yield an empty ledger (zero opening, empty page, zero closing) and a `200` — an empty result is a valid, common business state, not a `404`. The trial balance simply omits accounts with no in-window activity.
- An `accountId` that is not a positive integer MUST be rejected with `INVALID_ACCOUNT_ID` (400) before any query runs.
- A `404` MAY be returned only when the implementator chooses to assert account existence via the `IReferenceDataReader` seam and the account id is genuinely unknown; in that case the code MUST be `ACCOUNT_NOT_FOUND` (404). The DEFAULT and PREFERRED behavior is to return an empty ledger without an existence pre-check (cheaper, and a not-yet-used account is indistinguishable from a missing one for a read). The implementator MUST pick one and document it (§7); the spec's recommendation is the empty-ledger default.

### 2.5 Account enrichment via the existing seam (MUST)
- Account `Code` / `Name` (and type when available) MUST be obtained through the **existing** `IReferenceDataReader` / `IAccountReadClient` seam (`GET /api/v1/accounts/{id}` through the gateway) — the same seam SDD-FIN-001 §2.6 uses for postability. No new cross-service contract MUST be introduced and no cross-database join into `finance_accounts` MUST be performed (Plan §8).
- The seam MAY be extended with a narrow read (e.g., a batch `code/name` lookup for the set of `AccountId`s in a trial balance) to avoid N per-account round-trips; any such extension MUST stay on the existing Refit-through-gateway seam and MUST be additive. (Whether to add a batch read is an implementator decision — §7.)
- Enrichment MUST be resilient: if the Accounts read is unreachable for an account, the row MUST still be returned with its `AccountId` and numeric balances, with `AccountCode` / `AccountName` left null/blank (a degraded but correct balance is better than a failed report). Enrichment failure MUST NOT fail the whole query and MUST NOT be treated as `not-postable` (postability is a write-path concern, SDD-FIN-001; reads do not gate on it).

### 2.6 No caching (MUST — SDD-INFRA-004)
- GL balances, the trial balance, and the account ledger MUST NOT be cached. They are derived from transactional data (journal lines), which SDD-INFRA-004 forbids caching. Each request MUST recompute from the current `Posted` lines.
- Only the enrichment reference data (account code/name) MAY benefit from the Accounts service's own reference-data cache behind the gateway; that is the Accounts service's concern, not this read path. This spec MUST NOT introduce a cache keyed on balances/dates.

### 2.7 Cross-cutting obligations (MUST)
- Both endpoints MUST be protected by `[RequirePermission("finance.journal:read")]`, decoded via the shared `Warehouse.Auth.Shared` package (SDD-INT-AUTH-001). The chosen permission reuses the journal read permission because the GL/TB read the same service and the same data (a reader of journal entries is, by definition, a reader of the ledger they roll up to). See §7 for the considered alternative `finance.ledger:read` / `finance.gl:read`.
- `CorrelationId` MUST flow via `ICorrelationIdAccessor` / `CorrelationIdMiddleware`; outbound enrichment reads through the gateway MUST carry the `CorrelationIdDelegatingHandler` (SDD-INFRA-001) — already configured on the existing `IAccountReadClient`.
- The endpoints MUST be traced via OpenTelemetry with the `correlation_id` Activity tag and MUST use NLog structured templates — no string interpolation in log calls (SDD-OBS-001).
- `CancellationToken` MUST be threaded controller → service → query.

### 2.8 Edge cases (MUST)
- **Empty ledger.** An account with no posted lines in/before the window MUST return `OpeningBalance == 0.00`, an empty page, `ClosingBalance == 0.00`, and a `200` — never a `404` or an error (§2.4).
- **Draft entries excluded.** A `Draft` entry's lines MUST NOT contribute to any opening balance, in-range line, trial-balance total, or grand total. Posting that draft later MUST then make the lines appear; the read reflects current state.
- **Reversal nets to zero.** When an entry is posted and then reversed (original `Reversed`, reversal `Posted`), the affected account's net contribution from those two entries MUST be `0.00` in the trial balance, with both entries visible as separate lines in that account's ledger (one positive, one offsetting). The implementation MUST NOT special-case this — it MUST fall out of summing both `Posted` line sets.
- **Date-range boundary.** A posted entry whose `EntryDate` equals `fromDate` MUST be **included** in the in-range list (lower bound inclusive) and MUST NOT be counted in the opening balance (opening is strictly before `fromDate`). A posted entry whose `EntryDate` equals `toDate` (or `asOfDate`) MUST be included (upper bound inclusive). An entry on the day after `toDate` MUST be excluded.
- **Σ debit == Σ credit invariant.** For any trial balance over any window, `GrandTotalDebit` MUST equal `GrandTotalCredit` to the cent, and `Balanced` MUST be `true`, because every posted entry balances in base currency (SDD-FIN-001 §2.3). A test MUST construct multiple balanced posted entries across several accounts and assert the grand totals match.
- **Mixed-currency entries roll up in base.** A posted multi-currency entry (EUR debit, BGN credit, balanced in base per SDD-FIN-001 §2.9) MUST contribute its `Base*Amount` values to each account's totals; the trial balance MUST remain balanced. Transactional currency MUST NOT affect the balanced invariant.
- **`fromDate` after `toDate`.** A request where `fromDate > toDate` (account ledger) MUST be rejected with `INVALID_DATE_RANGE` (400) before any query runs.

## 3. Validation Rules

### 3.1 Field-level (FluentValidation — request shape)

| Endpoint | Field | Rule | Error code |
|---|---|---|---|
| Trial Balance | `asOfDate` | Required | `INVALID_DATE_RANGE` |
| Trial Balance | `fromDate` (optional) | When present, `fromDate ≤ asOfDate` | `INVALID_DATE_RANGE` |
| Account Ledger | `accountId` (route) | Positive integer (`> 0`) | `INVALID_ACCOUNT_ID` |
| Account Ledger | `fromDate` / `toDate` | When both present, `fromDate ≤ toDate` | `INVALID_DATE_RANGE` |
| Both | `FilterRequest.PageSize` | ≤ 200 (cap enforced by SDD-INFRA-005) | `PAGE_SIZE_TOO_LARGE` |

### 3.2 Cross-field / computed (read-path assertions)

| Rule | Mechanism | Surfaced as |
|---|---|---|
| `GrandTotalDebit == GrandTotalCredit` to the cent | computed over the aggregation | `Balanced` flag on the response (true on a consistent ledger; SDD-FIN-001 §2.3) |
| `ClosingBalance == OpeningBalance + Σ(in-range Debit − Credit)` | computed in the ledger projection | response consistency (asserted by test) |
| Only posted-to-ledger lines aggregated; `Draft` excluded | query predicate (`Status ∈ { Posted, Reversed }`) | balances reflect posted state only; reversed originals stay on the books and net against their reversal |

### 3.3 State-based

| Condition | Rule | Outcome |
|---|---|---|
| Account has no posted activity in window | Return empty ledger / omit from TB | `200`, zero balances (§2.4) — not an error |
| Enrichment read unreachable | Return numeric balances; null code/name | `200`, degraded enrichment (§2.5) — not an error |
| `accountId` not a positive integer | Reject before query | `INVALID_ACCOUNT_ID` (400) |
| `fromDate > toDate` / `fromDate > asOfDate` | Reject before query | `INVALID_DATE_RANGE` (400) |

## 4. Error Rules

All errors are RFC-7807 ProblemDetails per SDD-INFRA-001: `title` = code (SCREAMING_SNAKE_CASE), `detail` = developer English, `type` = `https://finance.local/errors/{code}`. The error-code → HTTP-status mapping is owned by the Journal service's `IErrorCodeToStatusMap` (SDD-INFRA-009); services return `Result.Failure(code, detail)`; `BaseApiController.ToActionResult` performs the mapping. This is a read API, so the error surface is intentionally minimal.

| Code | HTTP | Trigger | Type |
|---|---|---|---|
| `INVALID_DATE_RANGE` | 400 | `fromDate > toDate`, `fromDate > asOfDate`, or a required date missing | Validation (range) |
| `INVALID_ACCOUNT_ID` | 400 | Route `accountId` is not a positive integer | Validation (shape) |
| `PAGE_SIZE_TOO_LARGE` | 400 | `FilterRequest.PageSize` exceeds 200 (from SDD-INFRA-005) | Validation (paging) |
| `ACCOUNT_NOT_FOUND` | 404 | (OPTIONAL — only if the implementator opts into an account-existence pre-check via the seam; the PREFERRED default is an empty ledger, §2.4) | Not found |

`INVALID_DATE_RANGE` is already defined in the codebase (used by `SDD-NOM-001` exchange-rate range reads and `SDD-EVTLOG-001`); a Journal-domain constant MUST live in `Finance.Common.ErrorCodes.JournalErrorCodes` (or reference the shared constant — implementator decides where the single source sits, but the literal MUST NOT be a raw string — CLAUDE.md §0.3). `INVALID_ACCOUNT_ID` is a new constant in `JournalErrorCodes`. `PAGE_SIZE_TOO_LARGE` lives in `FilterErrorCodes` (SDD-INFRA-005, reused — NOT redefined). `ACCOUNT_NOT_FOUND` is added to `JournalErrorCodes` ONLY if the existence-check option is taken; otherwise it is omitted.

**Frontend obligation (no frontend in this batch unless the GL views ship together).** Every code above MUST get a matching `errors.<CODE>` entry in BOTH `frontend/src/shared/i18n/locales/en.ts` and `bg.ts` in the same PR as the GL/Trial-Balance frontend (SDD-UI-001). `INVALID_DATE_RANGE` already exists on the frontend (added for currencies/event-log); `INVALID_ACCOUNT_ID` (and `ACCOUNT_NOT_FOUND` if used) MUST be added when the frontend lands.

## 5. Versioning Notes

`/api/v1/trial-balance` and `/api/v1/general-ledger/accounts/{accountId}` are the v1 read surface.

- **v1 — Initial specification (Batch 12).** Read-only GL aggregation + trial balance over `Posted` journal lines in `Finance.Journal.API`. Base-currency-only sums on the `Base*Amount` columns; `Draft` excluded; reversals net naturally; per-account net column placement by sign; `Balanced` flag + grand totals; account ledger with opening balance, paged in-range lines (SDD-INFRA-005), running balance, closing balance; account code/name enrichment via the existing `IReferenceDataReader` seam; no caching; `finance.journal:read`.
- **Deferred (future versions / specs):**
  - **Normal-balance-side presentation** — placing each account's balance on its natural debit/credit side per account type (requires SDD-ACCT-001 account-type classification). Adding an optional `NormalSide` column is additive (non-breaking).
  - **Formatted/exported reports** — Balance Sheet, Income Statement, VAT journals (SDD-RPT-001/-002/-003) consume this aggregation; they are separate specs/endpoints, not a change to this surface.
  - **Materialized balance table** — a posting-event-fed running-balance snapshot for performance is a future `CHG-ENH-*`; it MUST preserve identical results to the live aggregation defined here.
  - **Transactional-currency breakdown / FX revaluation** — SDD-FIN-005.
- Adding a response field (e.g., account type, normal side, per-currency subtotal) is additive (non-breaking). Changing the aggregation semantics (e.g., including `Draft`, balancing in transactional currency, or excluding reversal entries) is breaking and requires `/api/v2/` plus a `CHG-ENH-*`.

## 6. Test Plan

> Environment: Docker/SQL/Redis/RabbitMQ are not available — only `[Unit]` tests run by default. EF unit tests use SQLite in-memory; the aggregation is pure `SELECT … GROUP BY` over seeded `Posted`/`Draft`/`Reversed` entries and needs no real broker. Account enrichment uses a faked `IReferenceDataReader` / `IAccountReadClient`. `WebApplicationFactory` HTTP tests and real-SQL aggregation tests carry `[Category("Integration")]` and are excluded from the default run. All business tests MUST reference `[Category("SDD-FIN-003")]`.

### 6.1 Trial balance aggregation (Unit — SQLite in-memory)

| Test name | Kind |
|---|---|
| `TrialBalance_MultipleBalancedEntries_GrandTotalsMatch_BalancedTrue` | [Unit] |
| `TrialBalance_NetDebitAccount_PlacedInDebitColumn` | [Unit] |
| `TrialBalance_NetCreditAccount_PlacedInCreditColumn` | [Unit] |
| `TrialBalance_ExcludesDraftEntries_FromAllTotals` | [Unit] |
| `TrialBalance_ReversedEntryAndReversal_NetToZero_NoSpecialCasing` | [Unit] |
| `TrialBalance_MultiCurrencyEntry_RollsUpInBaseCurrency_StaysBalanced` | [Unit] |
| `TrialBalance_AsOfDateUpperBoundInclusive_ExcludesLaterEntries` | [Unit] |
| `TrialBalance_FromDateLowerBoundInclusive_ExcludesEarlierEntries` | [Unit] |
| `TrialBalance_FromDateOmitted_AggregatesFromBeginningToAsOf` | [Unit] |
| `TrialBalance_OrdersRowsByAccountCodeAscending_Deterministic` | [Unit] |
| `TrialBalance_OnlyBaseAmountsSummed_TransactionalAmountsIgnored` | [Unit] |

### 6.2 GL account ledger (Unit — SQLite in-memory)

| Test name | Kind |
|---|---|
| `AccountLedger_OpeningBalance_SumsPostedLinesStrictlyBeforeFromDate` | [Unit] |
| `AccountLedger_FromDateOmitted_OpeningBalanceIsZero` | [Unit] |
| `AccountLedger_RunningBalance_AccumulatesDebitMinusCreditInLedgerOrder` | [Unit] |
| `AccountLedger_ClosingBalance_EqualsOpeningPlusInRangeNet` | [Unit] |
| `AccountLedger_NoPostings_ReturnsEmptyLedger_ZeroBalances_NotFound` | [Unit] |
| `AccountLedger_ExcludesDraftLines` | [Unit] |
| `AccountLedger_EntryOnFromDate_Included_NotInOpeningBalance` | [Unit] |
| `AccountLedger_EntryOnToDate_Included` | [Unit] |
| `AccountLedger_LinesOrderedByEntryDateThenPk_Deterministic` | [Unit] |
| `AccountLedger_RespectsPageSizeCap_200` | [Unit] |
| `AccountLedger_ReversalLine_AppearsAsOffsettingLine` | [Unit] |

### 6.3 Enrichment & validation (Unit — faked reference reader)

| Test name | Kind |
|---|---|
| `Enrichment_PopulatesAccountCodeAndName_FromReferenceDataReader` | [Unit] |
| `Enrichment_ReaderUnreachable_ReturnsBalancesWithNullCodeName_NoFailure` | [Unit] |
| `Validate_FromDateAfterToDate_ReturnsInvalidDateRange` | [Unit] |
| `Validate_FromDateAfterAsOfDate_ReturnsInvalidDateRange` | [Unit] |
| `Validate_NonPositiveAccountId_ReturnsInvalidAccountId` | [Unit] |
| `Validate_MissingAsOfDate_ReturnsInvalidDateRange` | [Unit] |
| `JournalErrorCodes_DefinesInvalidAccountId` | [Unit] |

### 6.4 Endpoint & wiring (Integration — `[Category("Integration")]`, excluded from default run)

| Test name | Kind |
|---|---|
| `TrialBalance_Returns200_WithBalancedTotals_OverRealSql` | [Integration] |
| `AccountLedger_Returns200_WithRunningBalance_OverRealSql` | [Integration] |
| `AccountLedger_Returns400_WhenFromDateAfterToDate` | [Integration] |
| `TrialBalance_Endpoint_Returns403_WhenPermissionMissing` | [Integration] |
| `AccountLedger_Endpoint_Returns403_WhenPermissionMissing` | [Integration] |
| `GlBalances_AreNotCached_RecomputeReflectsNewPosting` | [Integration] |

## 7. Resolved Decisions & Open Items

### Resolved
- **Lives in the existing Journal service.** GL/Trial-Balance are read-only aggregations over `finance_journal` — no new service, database, tables, events, audit rows, or workflow. They live in `Finance.Journal.API` (port 6004), per FINANCE-MICROSERVICES-PLAN §9 (Journal & GL engine, Phase 3).
- **Posted-to-the-ledger only, base-currency-only.** Lines aggregate when their owning entry's status is `Posted` ∪ `Reversed` (a reversed original stays on the books and is offset by its reversal entry); `Draft` is excluded; reversals net naturally with no special-casing (§2.1). Sums use `BaseDebitAmount` / `BaseCreditAmount` (`DECIMAL(18,2)`) — never the transactional amounts, never `double`/`float`.
- **Column placement by net sign.** v1 derives the debit/credit column purely from `net = TotalDebit − TotalCredit` sign; no account normal-balance-side classification is required.
- **`Balanced` invariant is a stated MUST + response field.** Σ TotalDebit == Σ TotalCredit follows from SDD-FIN-001 §2.3 and is asserted, not assumed.
- **Enrichment via the existing seam.** Account code/name come through the existing `IReferenceDataReader` / `IAccountReadClient` (gateway Refit) — no new cross-service mechanism, no cross-DB join.
- **No caching.** GL balances are transactional-derived → MUST NOT be cached (SDD-INFRA-004).
- **Permission.** `finance.journal:read` for both endpoints (same service, same data).
- **Endpoints.** `GET /api/v1/trial-balance` (query: `asOfDate`, optional `fromDate`) and `GET /api/v1/general-ledger/accounts/{accountId}` (route: `accountId`; query: `fromDate`, `toDate`, `FilterRequest`).

### Open / deferred (for the Phase-2 implementator)
- **Empty-vs-404 for unknown account.** The spec PREFERS returning an empty ledger (zero balances, `200`) without an account-existence pre-check — cheaper, and a not-yet-used account is indistinguishable from a missing one on a read. If the implementator instead opts to assert existence via the seam, it MUST surface `ACCOUNT_NOT_FOUND` (404) and document the choice. Decide once; default = empty ledger.
- **Batch vs per-account enrichment.** A trial balance touches many accounts; N per-account gateway reads may be slow. The implementator MAY add a narrow batch code/name read on the existing seam (additive, gateway-only) or accept per-account reads with the Accounts service's own reference cache absorbing the cost. No new cross-DB mechanism.
- **Permission name.** `finance.journal:read` is chosen. If a distinct GL/ledger permission is preferred for finer RBAC, `finance.ledger:read` or `finance.gl:read` are the considered alternatives (would require seeding the new permission in the auth-service — SDD-INT-AUTH-001). Revisit only if a stakeholder asks for read-segregation between raw journals and the ledger roll-up.
- **Zero-net account listing in TB.** Whether to list accounts whose net is exactly `0.00` (activity that cancels out) or omit them is a presentation choice; default recommendation is to **omit** accounts with no in-window posted lines, but **list** accounts that have activity netting to zero (so the ledger is auditable). Confirm with the reporting stakeholder when SDD-RPT-001 lands.
- **Normal-balance-side presentation.** Deferred to a future additive enhancement once SDD-ACCT-001 exposes a usable account-type / normal-side classification (shares the `AccountDto` enrichment gap noted in SDD-FIN-001 §7 / `CHG-ENH-002`).
- **Performance / materialization.** v1 aggregates live on read. If trial-balance latency over a large ledger becomes a problem, a posting-event-fed materialized balance table is a future `CHG-ENH-*` that MUST reproduce identical results to this live aggregation.
