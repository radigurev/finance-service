# SDD-FIN-001 — Double-Entry Engine

> Status: Implemented (Batch 10 — manual journal entries: double-entry invariants + Draft→Posted→Reversed lifecycle. Period-lock guard is an extension seam pending SDD-FIN-004; posting rules (FIN-006) and GL/trial-balance (FIN-003) are deferred.)
> Owner: Finance
> Last updated: 2026-06-03
> Category: Core
> Service: `Finance.Journal.API` — port **6004**, database `finance_journal`
> Related: SDD-FIN-002 (Journal Entry lifecycle — the state machine over the `JournalEntry` aggregate this spec defines), SDD-ACCT-001 (accounts are the posting targets), SDD-NOM-001 (currencies + exchange-rate reads), SDD-INFRA-001 (correlation, ProblemDetails, decimal arithmetic), SDD-INFRA-005 (filtering/paging), SDD-INFRA-007 (cross-aggregate validation chain), SDD-INFRA-009 (base service/controller, `Result<T>`), SDD-AUDIT-001 (immutability), SDD-INT-AUTH-001 (RBAC), SDD-FIN-003 (General Ledger & Trial Balance — future), SDD-FIN-005 (Multi-Currency Engine — Currency tables currently owned by SDD-NOM-001), SDD-FIN-006 (Posting Engine + rule-derived lines — future), SDD-CTRY-001 (country strategy — future)
> ISA-95: Level 4 (Business Planning & Logistics) — Bookkeeping

---

## 1. Context & Scope

The Double-Entry Engine is the country-agnostic invariant core of the Finance platform. Every financial transaction in the system — whether a manually-keyed adjustment, an invoice posting (SDD-INV-001), a payment allocation (SDD-PAY-001/002), or a rule-derived COGS entry (SDD-FIN-006) — is ultimately expressed as a **journal entry**: a balanced set of debit/credit lines against chart-of-accounts accounts (SDD-ACCT-001). This spec defines the two entities `JournalEntry` and `JournalEntryLine`, their persisted shape, and the **accounting invariants** that any journal entry MUST satisfy regardless of how its lines were produced.

The single non-negotiable invariant is the **double-entry rule**: for any journal entry, the sum of debit amounts MUST equal the sum of credit amounts. Because lines may carry different currencies, the balance check is enforced in the **base currency** — each line carries its transactional amount, its currency, an exchange rate, and a derived base-currency amount, and balance is asserted on the base-currency amounts to the cent (`DECIMAL(18,2)`).

This spec defines the **invariant layer only**. The lifecycle that moves an entry through `Draft → Posted → Reversed` (the workflow, the gapless number, the events, the audit rows, the period-lock guard seam) is owned by **SDD-FIN-002**, which builds directly on the entities and validation rules defined here. The two specs are intentionally split: FIN-001 is the pure "what makes a journal entry valid" layer; FIN-002 is the "what may happen to a journal entry over time" layer.

**ISA-95 classification.** `JournalEntry` and `JournalEntryLine` are ISA-95 **Level 4 (Business Planning & Logistics)** business-transaction artifacts (ISA-95 / IEC 62264 Part 1, §5 — Business Planning & Logistics; the double-entry posting is a financial business transaction, not a Level-3 MES production operation). A `JournalEntry` is the canonical Level-4 record of a financial state change; its lines reference Level-4 reference/master data (accounts, currencies). The validation operations defined here are pure invariant checks on a business-transaction aggregate; they emit no events themselves (state-change events belong to SDD-FIN-002, which carries the immutable-event obligation for posting and reversal).

**Scope — covered:**
- The `JournalEntry` aggregate root and its `JournalEntryLine` child entity: fields, types, PK strategy, EF Fluent mapping.
- The double-entry balance invariant (SUM debits = SUM credits in base currency, to the cent).
- Per-line invariants: a line carries a debit OR a credit (never both, never neither); no zero-amount lines; the referenced account MUST be valid and postable (active, non-header).
- Multi-currency line shape: transactional amount + currency + exchange rate + derived base-currency amount; balance enforced in base currency.
- The minimum-two-lines rule.
- The validation surface (`IJournalEntryValidator` / chain) that FIN-002's create and post paths call before persisting or posting.

**Scope — excluded (deferred):**
- The journal-entry **lifecycle / state machine** (`Draft → Posted → Reversed`), gapless numbering, posting/reversal events, audit rows, and the period-lock guard — owned by **SDD-FIN-002**.
- **Rule-derived lines** (posting templates that turn an invoice/payment into a balanced set of lines) — owned by **SDD-FIN-006** (Posting Engine). This batch covers **manual/explicit** journal entries only: the caller supplies the lines.
- **General Ledger aggregation and Trial Balance** (account balances rolled up from posted lines) — owned by **SDD-FIN-003** (future).
- **Exchange-rate sourcing / FX revaluation** — the engine consumes a rate supplied on each line (validated against SDD-NOM-001 exchange-rate reads where a rate lookup is wired); automatic rate resolution and period-end revaluation are SDD-FIN-005 concerns.
- **Tax computation and rounding** — country-strategy concern (SDD-CTRY-001); the engine validates already-computed amounts.
- Inter-company / consolidation eliminations, dimensions/analytics tags beyond the account reference — future.

## 2. Behavior

> **Service/controller contract (SDD-INFRA-009).** The journal-entry service inherits `SearchableServiceBase<JournalEntry, JournalEntryDto, JournalDbContext>` (and thus `BaseEntityService<JournalDbContext>`). Every public method MUST return `Result` / `Result<T>` — never `null`, never a thrown exception for a business outcome. The controller inherits `BaseApiController` and translates results via `ToActionResult(...)`. Error-code → HTTP-status mapping and the ProblemDetails shape are owned by SDD-INFRA-001. The mutation/lifecycle endpoints themselves are specified in SDD-FIN-002; this spec defines the entities and the invariant validation those endpoints invoke.

### 2.1 `JournalEntry` aggregate (MUST)
- The `JournalEntry` aggregate root MUST carry at minimum:
  - `Id` (`UNIQUEIDENTIFIER`, PK, `NEWSEQUENTIALID()` default) — the entry is event-exposed (SDD-FIN-002 publishes `JournalEntryPostedEvent` / `JournalEntryReversedEvent`) and externally referenced, so it MUST be a sequential GUID, NOT `INT IDENTITY`.
  - `EntryNumber` (`string`, nullable until posting) — the gapless document number assigned at posting time by `ISequenceGenerator` (key `JE`, SDD-INFRA-003); NULL while `Draft`. Assignment is owned by SDD-FIN-002.
  - `EntryDate` (`DATETIMEOFFSET`) — the accounting date of the transaction (the date used for period assignment in SDD-FIN-002/FIN-004), distinct from row-creation time.
  - `Description` (`string`) — a human-readable memo.
  - `BaseCurrencyCode` (`string`, 3-char ISO 4217) — the base currency the entry balances in; sourced from configuration (`Country:BaseCurrency`) at creation and frozen on the entry.
  - `Status` (`string` / enum-backed: `Draft`, `Posted`, `Reversed`) — the lifecycle state; transitions owned by SDD-FIN-002.
  - `ReversesEntryId` (`UNIQUEIDENTIFIER?`) — set on a reversal entry, pointing at the original entry it reverses (SDD-FIN-002 §2.5). NULL on ordinary entries.
  - `CorrelationId` (`UNIQUEIDENTIFIER` / `string`) — the ambient correlation id captured at creation (SDD-INFRA-001).
  - `RowVersion` (`byte[]`, `rowversion`) — optimistic-concurrency token (SDD-INFRA-008/009).
  - `CreatedAt` (`DATETIMEOFFSET`, `SYSDATETIMEOFFSET()` default), `CreatedBy`. `PostedAt` / `PostedBy` are set by SDD-FIN-002 at posting.
- The aggregate MUST own a collection of `JournalEntryLine` (composition: lines have no independent lifecycle and are loaded/saved with the entry).
- All EF mapping MUST be via Fluent API (`JournalEntryConfiguration`, `JournalEntryLineConfiguration`) in the `finance_journal` database / `journal` schema — NO conventions, NO Data Annotations.

### 2.2 `JournalEntryLine` entity (MUST)
- Each `JournalEntryLine` MUST carry at minimum:
  - `Id` (`INT IDENTITY`, PK) — internal child reference, not externally exposed.
  - `JournalEntryId` (`UNIQUEIDENTIFIER`, FK → `JournalEntry.Id`).
  - `AccountId` (FK → `accounts.Id`, SDD-ACCT-001) — the posting target. Cross-database join is NOT performed; account validity is asserted via the validation surface (§2.6) using the account reference available to the Journal service.
  - `DebitAmount` (`DECIMAL(18,2)`) and `CreditAmount` (`DECIMAL(18,2)`) — the transactional amounts; exactly one MUST be non-zero (§2.4). Monetary fields MUST be `decimal` in C# / `DECIMAL(18,2)` in SQL — never `float`/`double` (SDD-FIN-005 / SDD-INFRA-001).
  - `CurrencyCode` (`string`, 3-char ISO 4217) — the line's transactional currency.
  - `ExchangeRate` (`DECIMAL(18,6)`) — the rate from the line currency to the entry's `BaseCurrencyCode`. For a line already in the base currency the rate MUST be `1.000000`.
  - `BaseDebitAmount` (`DECIMAL(18,2)`) and `BaseCreditAmount` (`DECIMAL(18,2)`) — the base-currency equivalents (`DebitAmount × ExchangeRate` / `CreditAmount × ExchangeRate`, rounded to 2 decimals). The balance check (§2.3) runs on these.
  - `LineNumber` (`int`) — 1-based ordinal for stable display ordering.
  - `Description` (`string?`) — optional per-line memo.
- A line MUST belong to exactly one `JournalEntry`.

### 2.3 Double-entry balance invariant (MUST — the core rule)
- For any `JournalEntry`, `SUM(line.BaseDebitAmount)` MUST equal `SUM(line.BaseCreditAmount)`.
- Equality MUST be asserted to the **cent** — i.e., on values rounded to 2 decimal places (`DECIMAL(18,2)`). A residual difference of `0.00` is balanced; any non-zero difference (even `0.01`) MUST fail with `UNBALANCED_ENTRY`.
- The balance check MUST be performed in **base currency** (the `Base*` amounts), NOT in the transactional currency — a multi-currency entry whose transactional debits and credits differ but whose base-currency debits and credits are equal is **balanced**.
- This invariant MUST be enforced before an entry may be created in `Draft` and again (defensively) before it may be posted (SDD-FIN-002). The check MUST be re-run at post time because nothing guarantees the lines were not altered while `Draft`.

### 2.4 Per-line debit-XOR-credit invariant (MUST)
- Each line MUST have **either** a non-zero `DebitAmount` **or** a non-zero `CreditAmount`, never both:
  - If both `DebitAmount > 0` and `CreditAmount > 0`, the line MUST fail with `LINE_DEBIT_AND_CREDIT_SET`.
  - If both `DebitAmount == 0` and `CreditAmount == 0`, the line MUST fail with `LINE_HAS_NO_AMOUNT` (no zero-amount lines).
- Negative amounts MUST NOT be supplied on either side. A correction that needs a negative effect is expressed by placing the amount on the opposite side (a debit becomes a credit), not by a negative number. A negative `DebitAmount` or `CreditAmount` MUST fail with `LINE_HAS_NO_AMOUNT` (treated as not a valid positive amount on its side).
- The corresponding `BaseDebitAmount` / `BaseCreditAmount` MUST be populated on exactly the same side as the transactional amount.

### 2.5 Minimum-two-lines invariant (MUST)
- A `JournalEntry` MUST contain at least 2 lines. A single-line entry can never balance and is meaningless in double-entry; fewer than 2 lines MUST fail with `MIN_TWO_LINES_REQUIRED`.
- There is no upper bound on line count in this spec (a complex posting may have many lines), subject to the page/request-size limits of the transport.

### 2.6 Account postability invariant (MUST — SDD-ACCT-001)
- Every line's `AccountId` MUST reference an account that exists AND is **postable**. An account is postable when it is **active** (`IsActive == true`) AND is **not a header/parent account** (it is a leaf account that may carry postings). A line referencing a missing, inactive, or header account MUST fail with `ACCOUNT_NOT_POSTABLE`.
- The "non-header" determination is the account-hierarchy rule from SDD-ACCT-001: an account that has children (or is otherwise flagged as a roll-up/header) MUST NOT be a direct posting target. (SDD-ACCT-001 today exposes `ParentId`; "postable" = leaf + active. The precise header flag is a resolved decision for the implementator — see §7.)
- This is a **cross-aggregate** check (it depends on account state, not just the line shape) and therefore MUST run through an `IChainValidator` (SDD-INFRA-007), not inline FluentValidation.
- **Shipped-scope note (Batch 10).** The implementation enforces `exists AND IsActive` only. The leaf/non-header half of this MUST is **deferred**: the `AccountDto` returned by the SDD-ACCT-001 read endpoints exposes neither an `IsHeader`/`IsPostable` flag nor a child count, so a header account cannot be detected through the existing contract. The postability seam already accepts the future flag with no posting-code change; activation is tracked under `CHG-ENH-002` (add `IsHeader`/`IsPostable` to `AccountDto`). Until then, an active header account will not be rejected.
- **Fail-closed reference reads.** When the reference-data source (account / currency lookup) is unreachable or returns a non-404 error, the line resolves to **not-postable** / **not-valid** — an unverified reference MUST NOT post. This is the safe default, enforced by `GatewayReferenceDataReader`.

### 2.7 Multi-currency handling (MUST)
- A line's `CurrencyCode` MUST be a valid, active currency (SDD-NOM-001). When `CurrencyCode == BaseCurrencyCode`, `ExchangeRate` MUST be exactly `1.000000` and `Base*Amount` MUST equal the transactional amount.
- When `CurrencyCode != BaseCurrencyCode`, `ExchangeRate` MUST be `> 0` and the `Base*Amount` MUST equal the transactional amount multiplied by the rate, rounded to 2 decimals. A `Base*Amount` that does not reconcile with `transactionalAmount × ExchangeRate` (within the rounding tolerance of half a cent) MUST fail with `INVALID_LINE_BASE_AMOUNT`.
- The engine consumes the rate supplied on the line (manual entries carry an explicit rate). Automatic rate resolution from SDD-NOM-001 / SDD-INT-BNB-001 and FX revaluation are deferred (SDD-FIN-005).

### 2.8 Validation surface (MUST)
- The invariants in §2.3–2.7 MUST be exposed as a single validation surface that FIN-002's create and post operations invoke. Shape-only, single-line invariants (§2.4, §2.5 — debit-XOR-credit, no-zero, min-two-lines) MUST be expressed in FluentValidation with `.WithErrorCode(...)` referencing constants in `Finance.Common/ErrorCodes/JournalErrorCodes.cs`. Stateful / cross-aggregate invariants (§2.3 balance computed across lines, §2.6 account postability, §2.7 currency validity) MUST run through `IChainValidator<...>` (SDD-INFRA-007) so the first failure short-circuits to `Result.Failure(code, detail)`.
- The validation surface MUST be **pure** with respect to lifecycle: it MUST NOT change state, publish events, or write audit/sequence rows. Those are SDD-FIN-002 side effects.

### 2.9 Edge cases (MUST)
- **Multi-currency balanced entry.** An entry with a EUR debit line and a BGN credit line whose base-currency amounts are equal MUST validate as balanced (the transactional amounts differ; the base amounts match). This is the canonical multi-currency happy path.
- **Off-by-a-cent rounding.** An entry whose base debits total `100.00` and base credits total `100.01` MUST fail with `UNBALANCED_ENTRY` — the balance check is exact to the cent, never "close enough".
- **All-on-one-side.** An entry whose lines are all debits (or all credits) MUST fail with `UNBALANCED_ENTRY` (the opposite side sums to zero), even if it has ≥ 2 lines.
- **Header-account target.** A line posting to a parent/header account MUST fail with `ACCOUNT_NOT_POSTABLE` even if every other invariant holds.
- **Rate of zero on a foreign line.** A foreign-currency line with `ExchangeRate == 0` MUST fail (`INVALID_LINE_BASE_AMOUNT`) because the base amount cannot reconcile and a zero rate is never valid.

## 3. Validation Rules

### 3.1 Field-level (FluentValidation — shape, per line / per entry)

| Target | Field | Rule | Error code |
|---|---|---|---|
| Line | `DebitAmount` / `CreditAmount` | Exactly one non-zero; both-set rejected | `LINE_DEBIT_AND_CREDIT_SET` |
| Line | `DebitAmount` / `CreditAmount` | At least one non-zero; negatives rejected | `LINE_HAS_NO_AMOUNT` |
| Line | `CurrencyCode` | 3 uppercase letters (`^[A-Z]{3}$`) | `INVALID_LINE_CURRENCY` |
| Line | `ExchangeRate` | `> 0`; `== 1.000000` when line currency == base | `INVALID_LINE_BASE_AMOUNT` |
| Entry | `Lines` | Count ≥ 2 | `MIN_TWO_LINES_REQUIRED` |
| Entry | `EntryDate` | Required | `INVALID_ENTRY_DATE` |

### 3.2 Cross-field / cross-aggregate (validation chain — SDD-INFRA-007)

| Rule | Mechanism | Error code |
|---|---|---|
| SUM(base debits) == SUM(base credits), to the cent | chain (computed across all lines) | `UNBALANCED_ENTRY` |
| Each line's account exists, is active, and is a leaf/postable account | chain (account lookup, SDD-ACCT-001) | `ACCOUNT_NOT_POSTABLE` |
| Each line's currency exists and is active | chain (currency lookup, SDD-NOM-001) | `INVALID_LINE_CURRENCY` |
| Each line's `Base*Amount` reconciles with `amount × rate` (±½ cent) | chain (per-line recompute) | `INVALID_LINE_BASE_AMOUNT` |

### 3.3 State-based

| Condition | Rule | Owner |
|---|---|---|
| Balance + postability re-checked at post time | MUST re-run §3.1–3.2 before `Draft → Posted` | SDD-FIN-002 (invokes this surface) |
| Lines on a `Posted` entry | Immutable — never edited; correct via reversal | SDD-FIN-002 / SDD-AUDIT-001 |

## 4. Error Rules

All errors are emitted as RFC-7807 ProblemDetails per SDD-INFRA-001: `title` = code (SCREAMING_SNAKE_CASE), `detail` = developer English, `type` = `https://finance.local/errors/{code}`. The error-code → HTTP-status mapping is owned by `DefaultErrorCodeToStatusMap` (SDD-INFRA-009): validation/shape → 400; `*_NOT_*`/postability conflicts as noted below. Services return `Result.Failure(code, detail)`; `BaseApiController.ToActionResult` performs the mapping.

| Code | HTTP | Trigger | Type |
|---|---|---|---|
| `UNBALANCED_ENTRY` | 400 | SUM(base debits) ≠ SUM(base credits) to the cent | Validation (balance) |
| `LINE_DEBIT_AND_CREDIT_SET` | 400 | A line carries both a debit and a credit amount | Validation (shape) |
| `LINE_HAS_NO_AMOUNT` | 400 | A line has neither a positive debit nor credit (incl. negative/zero) | Validation (shape) |
| `MIN_TWO_LINES_REQUIRED` | 400 | Fewer than 2 lines | Validation (shape) |
| `ACCOUNT_NOT_POSTABLE` | 409 | Line account missing, inactive, or a header/parent account | Conflict (cross-aggregate) |
| `INVALID_LINE_CURRENCY` | 400 | Line currency malformed or not an active currency | Validation (shape/chain) |
| `INVALID_LINE_BASE_AMOUNT` | 400 | Base amount does not reconcile with amount × rate, or rate ≤ 0 on a foreign line | Validation (chain) |
| `INVALID_ENTRY_DATE` | 400 | `EntryDate` missing | Validation (shape) |

`ACCOUNT_NOT_POSTABLE` is mapped to **409 Conflict** (suffix family `*_NOT_POSTABLE` is a state conflict, consistent with SDD-ACCT-001's `ACCOUNT_INACTIVE` → 409): the request shape is valid but the referenced account's state forbids posting. All other codes are 400 (shape/validation). The `DefaultErrorCodeToStatusMap` MUST be extended to map `ACCOUNT_NOT_POSTABLE` → 409 (it does not match the default `*_NOT_FOUND` → 404 pattern).

**Frontend obligation (no frontend in this batch).** When the journal-entry frontend is built, every code above MUST have a matching `errors.<CODE>` entry in BOTH `frontend/src/shared/i18n/locales/en.ts` and `bg.ts`, added in the same PR as the frontend (SDD-UI-001). This batch is backend-only; the obligation is recorded here for the frontend phase.

Constants live in `Finance.Common.ErrorCodes.JournalErrorCodes` (shared with SDD-FIN-002). `CONCURRENT_MODIFICATION` (used by the lifecycle, FIN-002) lives in `CommonErrorCodes` (single source, SDD-INFRA-008/009).

## 5. Versioning Notes

`/api/v1/journal-entries/*` is the v1 surface; the mutating endpoints are defined in SDD-FIN-002.

- **v1 — Initial specification (Batch 10).** Defines the `JournalEntry` + `JournalEntryLine` entities, the double-entry balance invariant (enforced in base currency, to the cent), the per-line debit-XOR-credit and no-zero-amount rules, the minimum-two-lines rule, account postability, and multi-currency line shape. Manual/explicit entries only (caller supplies lines).
- **Deferred (future versions / specs):** rule-derived lines (SDD-FIN-006); GL aggregation + trial balance (SDD-FIN-003); automatic exchange-rate resolution + FX revaluation (SDD-FIN-005); tax computation/rounding (SDD-CTRY-001); analytic dimensions beyond the account reference.
- Adding a new optional line field (e.g., a dimension tag) is additive (non-breaking). Changing the balance semantics (e.g., allowing transactional-currency balancing) or removing a field is breaking and requires `/api/v2/` plus a `CHG-ENH-*`.

## 6. Test Plan

> Environment: Docker/SQL/Redis/RabbitMQ are not available — only `[Unit]` tests run by default. EF unit tests use SQLite in-memory; the balance/shape invariants are pure and need no database. `WebApplicationFactory` HTTP tests (need auth-service/SQL/Redis/RabbitMQ) carry `[Category("Integration")]` and are excluded from the default run. All business tests MUST reference `[Category("SDD-FIN-001")]`.

### 6.1 Balance & line invariants (Unit)

| Test name | Kind |
|---|---|
| `Validate_BalancedSingleCurrencyEntry_Succeeds` | [Unit] |
| `Validate_BalancedMultiCurrencyEntry_BalancesInBaseCurrency_Succeeds` | [Unit] |
| `Validate_BaseDebitsNotEqualBaseCredits_ReturnsUnbalancedEntry` | [Unit] |
| `Validate_OffByOneCent_ReturnsUnbalancedEntry` | [Unit] |
| `Validate_AllLinesOnDebitSide_ReturnsUnbalancedEntry` | [Unit] |
| `Validate_LineWithBothDebitAndCredit_ReturnsLineDebitAndCreditSet` | [Unit] |
| `Validate_LineWithZeroAmounts_ReturnsLineHasNoAmount` | [Unit] |
| `Validate_LineWithNegativeAmount_ReturnsLineHasNoAmount` | [Unit] |
| `Validate_EntryWithSingleLine_ReturnsMinTwoLinesRequired` | [Unit] |
| `Validate_EntryWithNoLines_ReturnsMinTwoLinesRequired` | [Unit] |
| `Validate_MissingEntryDate_ReturnsInvalidEntryDate` | [Unit] |

### 6.2 Account & currency cross-aggregate invariants (Unit — SQLite in-memory / mocked lookups)

| Test name | Kind |
|---|---|
| `Validate_LineToInactiveAccount_ReturnsAccountNotPostable` | [Unit] |
| `Validate_LineToHeaderAccount_ReturnsAccountNotPostable` | [Unit] |
| `Validate_LineToMissingAccount_ReturnsAccountNotPostable` | [Unit] |
| `Validate_LineToLeafActiveAccount_Succeeds` | [Unit] |
| `Validate_LineWithMalformedCurrency_ReturnsInvalidLineCurrency` | [Unit] |
| `Validate_LineWithInactiveCurrency_ReturnsInvalidLineCurrency` | [Unit] |

### 6.3 Multi-currency reconciliation (Unit)

| Test name | Kind |
|---|---|
| `Validate_BaseCurrencyLine_RequiresRateOfOne` | [Unit] |
| `Validate_ForeignLine_BaseAmountReconcilesWithAmountTimesRate_Succeeds` | [Unit] |
| `Validate_ForeignLine_BaseAmountMismatch_ReturnsInvalidLineBaseAmount` | [Unit] |
| `Validate_ForeignLine_ZeroRate_ReturnsInvalidLineBaseAmount` | [Unit] |

### 6.4 EF mapping & error mapping (Unit)

| Test name | Kind |
|---|---|
| `JournalEntryConfiguration_MapsIdAsSequentialGuid` | [Unit] |
| `JournalEntryConfiguration_ConfiguresRowVersionConcurrencyToken` | [Unit] |
| `JournalEntryLineConfiguration_MapsAmountsAsDecimal18_2` | [Unit] |
| `JournalEntryLineConfiguration_MapsExchangeRateAsDecimal18_6` | [Unit] |
| `DefaultErrorCodeToStatusMap_MapsAccountNotPostableTo409` | [Unit] |

### 6.5 Persistence (Integration — `[Category("Integration")]`, excluded from default run)

| Test name | Kind |
|---|---|
| `Persist_JournalEntryWithLines_RoundTrips_WithCascadeOnLines` | [Integration] |
| `Persist_DecimalAmounts_RetainTwoDecimalPrecision_OnRealSql` | [Integration] |

## 7. Resolved Decisions & Open Items

### Resolved
- **Entities live in the Journal service.** `JournalEntry` + `JournalEntryLine` belong to `Finance.Journal.API` / `Finance.Journal.DBModel` (database `finance_journal`, schema `journal`), per FINANCE-MICROSERVICES-PLAN §2 (service #4) and Appendix A.
- **PK strategy.** `JournalEntry.Id` is `UNIQUEIDENTIFIER` + `NEWSEQUENTIALID()` (event-exposed, externally referenced). `JournalEntryLine.Id` is `INT IDENTITY` (internal child) — per CLAUDE.md §0.1 PK policy and Plan Appendix A.
- **Balance currency.** The balance invariant is enforced in **base currency** on the `Base*Amount` columns, to the cent. Transactional-currency balancing is explicitly NOT supported.
- **Validation split.** Shape/single-line rules → FluentValidation; balance + account postability + currency validity → `IChainValidator` (SDD-INFRA-007).

### Open (for the Phase-2 implementator)
- **Account "postable" determination.** SDD-ACCT-001 exposes `ParentId` and `IsActive` but does not yet expose an explicit `IsHeader` / `IsPostable` flag. The implementator MUST decide whether "non-header" is derived (account has no children) or requires a new SDD-ACCT-001 field. If a new field is needed, raise a `CHG-ENH-*` against SDD-ACCT-001 rather than inferring it silently. Until resolved, "postable = active AND leaf (no children)" is the working definition.
- **Cross-service account/currency lookup.** The Journal service owns `finance_journal` only; accounts live in `finance_accounts` and currencies in `finance_nomenclature`. Cross-database joins are forbidden (Plan §8). The implementator MUST decide how the postability/currency chain validators obtain account/currency state — a Refit read through the gateway, a locally-cached reference snapshot, or a denormalized reference table fed by the Account/Currency events (SDD-INT-WH / SDD-EVTLOG patterns). This is a design decision shared with SDD-FIN-002 §7.
- **`INVALID_LINE_BASE_AMOUNT` rounding tolerance.** Working tolerance is ½ cent on the recompute; confirm against `ICountryStrategy.ApplyTaxRounding` semantics when SDD-CTRY-001 lands.
