# SDD-PAY-003 — AP/AR Aging & Counterparty Balances

> Status: Implemented (Batch 17 — Phase 5, Payments: backend shipped + green `[Unit]` tests + validated spec↔code↔tests, per the CLAUDE.md §0 lifecycle (`Implemented` = code shipped + tests pass + in force, and MAY carry explicit `Deferred:` notes). Shipped inside the EXISTING `Finance.Payments.API`: the three read endpoints (`OpenItemsController`, `AgingController`, `CounterpartyBalancesController`, all `BaseApiController`-derived with 200/400/403 `[ProducesResponseType]` sets and `CancellationToken` last), the single `AgingService` aggregation path that `/aging` and `/counterparty-balances` both fold through (so they cannot drift, §2.7), the pure `AgingBucketCalculator` plus its supporting value types (`AgingBucketDefinition`, `AgingScope`, `OpenItemAggregateRow`, `OpenItemValuation`, `CounterpartyAggregate`), the `AgingMappingProfile`, the three FluentValidation validators over the shared `AgingQueryRules`, the nine `Finance.ServiceModel/Payments` query/report contracts (`OpenItemQueryRequest`, `AgingReportQueryRequest`, `CounterpartyBalanceQueryRequest`, `OpenItemDto` — the solution's only open-item shape — `AgingReportDto`, `AgingRowDto`, `AgingBucketAmountDto`, `AgingBucketTotalDto`, `CounterpartyBalanceDto`), the five additive `PaymentErrorCodes` aging codes, and the three gateway routes `open-items-route` / `aging-route` / `counterparty-balances-route`. As §2/§5 require, it added NO table, migration, event, consumer, audit row, workflow state, enum, or cache prefix, and `AgingService` takes no `ICacheService<T>`, `IWorkflowEngine<T>`, `IAuditService`, `IPublishEndpoint`, or `ISequenceGenerator`. Test evidence: 68 `[Category("SDD-PAY-003")]` test methods across six fixtures — `AgingOpenItemTests` (14), `AgingBucketCalculatorTests` (12), `AgingReportTests` (15), `AgingAsOfDateTests` (11), `AgingValidationTests` (9), `AgingQueryValidatorTests` (7) — plus the three aging-tagged tests in the shared `PaymentErrorCodesTests`, `PaymentErrorCodeToStatusMapTests`, and `PaymentControllerContractTests` fixtures, all within this batch's 286 green Payments unit tests (Phase-4 verified); the EN/BG `errors.*` entries for the five aging codes are covered by `frontend/src/shared/i18n/paymentErrorCodes.test.ts` (§4). Deferred: the §6.6 `[Category("Integration")]` endpoint suite — no `Finance.Payments.API.Tests/Integration/` folder exists yet, so the three `*_Endpoint_Returns403_WhenPermissionMissing` RBAC checks, the real-SQL aggregation checks, the no-caching recompute check, the cancellation-event exclusion check, and the gateway-routing check are all UNCOVERED (the same deliberate sequencing SDD-FIN-006 used in Batch 14, whose suite landed in Batch 15 — §6.6); materialized / indexed aging views (§5); counterparty STATEMENTS (Phase 7, SDD-RPT-*); credit limits / credit blocking / dunning / overdue interest (§5); unallocated-payment (on-account cash) aging (§2.10, §5, §7); `FilterRequest` row paging for `/aging` (§7); and the MANUAL `finance.aging:read` seeding in auth-service — now RECORDED in `CHG-ENH-003` but not yet PERFORMED, so until an operator runs those steps `/aging` and `/counterparty-balances` return `403` for every caller (§2.9).)
> Owner: Finance
> Last updated: 2026-08-05
> Category: Domain
> Service: `Finance.Payments.API` — port **6006**, database `finance_payments`, schema `payments` (FINANCE-MICROSERVICES-PLAN §2, service #6 = "Payments + Allocations + Matching"; Plan Phase 5 — "AP/AR aging, counterparty balances")
> Related: SDD-PAY-001 (Payment Recording & Lifecycle — owns the `Payment` aggregate, its `Direction`/`Status`/`Amount`/`BaseAmount`/`ExchangeRate` columns, the `Finance.Payments.API` host, the `PaymentsDbContext`, the `PaymentErrorCodes.cs` file this spec adds codes to, and the gateway `payments-cluster`; this spec adds NO service, NO table, and NO host wiring of its own), SDD-PAY-002 (Payment Allocation & Settlement — owns the `PaymentAllocation` rows and the local, event-fed `InvoiceOpenItem` read projection with `GrossTotal`/`SettledAmount`/`DueDate`/`BookingExchangeRate`/`InvoiceStatus`, and owns the shared `SettlementPairing` table (SDD-PAY-002 §2.5 rule 10) whose settleable document types bound the population aged here — its SDD-PAY-002 §2.3 `InvoiceConfirmedEvent` consumer creates NO open item for a document type no payment can settle, so in v1 a `CreditNote` never enters the projection at all (§2.1); this spec AGGREGATES that projection, does NOT redefine it, never writes to it, and inherits its eventual consistency), SDD-INV-001 (Invoice Lifecycle — the upstream documents whose `Confirmed`/`Posted` states the projection mirrors; `Cancelled`, `Reversed` and `Draft` invoices are excluded from every total here — the `Reversed` mirror arrives through the new `InvoiceReversedEvent` SDD-INV-001 §2.7 publishes in this batch and the fourth projection consumer SDD-PAY-002 §2.3 registers for it; its `Invoice.ExchangeRate` column, frozen at invoice creation from the transaction-date rate (SDD-INV-001 §2.14), is the ultimate source of the `BookingExchangeRate` this spec converts with, so `BaseOutstanding` is a real booking-rate figure for foreign-currency invoices, not a placeholder), SDD-FIN-003 (General Ledger & Trial Balance — the structural precedent this spec mirrors: a read-only aggregation added inside an existing service with no new tables, events, audit rows, or workflow; the GL's AR/AP control-account balances are the ledger-side counterpart of this sub-ledger view), SDD-INFRA-005 (filtering/paging — the two list endpoints use `FilterRequest`→`PagedResult` with the projection key appended as the final deterministic sort term), SDD-INFRA-007 (cross-aggregate validation chain — does NOT apply: every rule here is shape-only and stays in FluentValidation, no `IChainValidator` chain is registered, §3), SDD-INFRA-009 (base service/controller, `Result<T>`, `SearchableServiceBase`, `BaseApiController`), SDD-INFRA-001 (correlation, ProblemDetails, error-code constants — never raw literals), SDD-INFRA-004 (caching rules — aging is derived from transactional data and MUST NOT be cached), SDD-INT-AUTH-001 (RBAC — `finance.payment:read` plus the NEW `finance.aging:read`, which MUST be seeded MANUALLY in the auth-service because permission auto-registration is still deferred), SDD-CTRY-001 (Country Strategy — `ICountryStrategy.BaseCurrencyCode` supplies the reporting base currency and `ApplyTaxRounding` performs the base-currency rounding; this spec does NOT grow the interface), SDD-FIN-005 (Multi-Currency Engine / decimal arithmetic — not yet authored; `decimal`/`DECIMAL(18,2)` amounts and `DECIMAL(18,6)` rates apply here, and period-end FX revaluation of outstanding balances is deferred to it), SDD-AUDIT-001 (no audit rows are written — the immutable-audit obligation applies to state changes, and this spec changes no state), SDD-INFRA-006 (no events are published — this spec only reads the projection those idempotent consumers maintain), SDD-OBS-001 (tracing, structured logging), SDD-RPT-001 (Reporting — **not yet authored**; formatted/exported AP-AR reports and counterparty statements will consume this raw aggregation once it exists; this spec is the in-service read primitive, not the report rendering), SDD-INT-WH-002 (outbound Warehouse calls — not yet authored; counterparty name/address enrichment is deferred to it, so v1 returns the counterparty GUID only)
> ISA-95: Level 4 (Business Planning & Logistics) — Reporting

---

## 1. Context & Scope

AP/AR aging and counterparty balances are the **read-only roll-up** over the payments sub-ledger. SDD-PAY-001 defines the `Payment` aggregate and its `Draft → Confirmed → Posted` lifecycle; SDD-PAY-002 defines the `PaymentAllocation` matching rows and the local, event-fed **`InvoiceOpenItem`** projection that mirrors the Invoices service's documents inside `finance_payments` so that matching and aging never cross-join another service's database (Plan §8). This spec defines the three query primitives that turn those tables into an outstanding-balance view:

1. **Open items** — the filtered/paged list of individual invoices that still carry an outstanding amount as of a date, each with its days-past-due and its aging bucket. This is the drill-down behind a single aging cell.
2. **Aging** — the bucketed roll-up: for one direction (`AR` or `AP`), the outstanding amount per counterparty per aging bucket (`Current`, `1-30`, `31-60`, `61-90`, `90+` by default), plus report-level bucket totals.
3. **Counterparty balances** — one row per counterparty per currency: total outstanding, overdue outstanding, open-item count, and the oldest due date.

This is a **read-only aggregation**. It owns **no new tables**, publishes **no events**, writes **no audit rows**, runs **no workflow**, and introduces **no new enum** — it is a `SELECT … GROUP BY` over the `payments`-schema tables that SDD-PAY-001/-002 already own, hosted by the `Finance.Payments.API` those specs already stand up. All arithmetic is in `decimal` / `DECIMAL(18,2)` for amounts and `DECIMAL(18,6)` for rates, never `double`/`float` (SDD-FIN-005 / SDD-INFRA-001 / CLAUDE.md §0.3).

**The outstanding amount is the projection's own arithmetic.** For every open item, `Outstanding = GrossTotal − SettledAmount` — the two columns SDD-PAY-002 maintains on `InvoiceOpenItem` (`SettledAmount` is advanced by the allocate path and reduced by the deallocate path, and over-allocation is forbidden, so `Outstanding` is always in `[0.00, GrossTotal]`). This spec MUST NOT recompute settlement from scratch on the hot path and MUST NOT introduce a second notion of "settled".

**Only invoices that are legally in force are aged.** An open item counts only when its mirrored `InvoiceStatus` is `Confirmed` or `Posted` (SDD-INV-001 §2.1). `Draft` invoices never reach the projection at all; `Cancelled` **and `Reversed`** invoices are excluded from every open-item row, bucket, counterparty total, and grand total. This mirrors SDD-FIN-003's inclusion predicate discipline: the eligible-status set is stated once (§2.1) and every endpoint shares it. Invoice **reversal** is inside that exclusion because this batch closes the gap that used to hide it: SDD-INV-001 §2.7 publishes a new `InvoiceReversedEvent` when a fully-offsetting credit note drives the original `Posted → Reversed`, and SDD-PAY-002 §2.3 registers the fourth idempotent consumer that flips the mirrored `InvoiceStatus` to `Reversed`. A reversed document's ledger effect has been fully offset by its correcting note, so aging it would report an outstanding amount against a document that carries no ledger balance. This spec adds neither the event nor the consumer — it only reads the mirrored status they maintain, and both are hard prerequisites of §2.1 (§7).

**Only documents a payment can actually settle are aged.** An outstanding balance is a claim that some future payment will discharge, so the aged population is exactly the set of document types SDD-PAY-002's shared `SettlementPairing` table admits — in v1 `SaleInvoice`, `PurchaseInvoice`, and `DebitNote`. `CreditNote` is NOT in that set: `SettlementPairing` allows `CustomerReceipt → { SaleInvoice, DebitNote }` and `SupplierPayment → { PurchaseInvoice }` only, so no payment can ever reduce a credit note's outstanding amount. Aging one would report a balance that can never reach `0.00` and that no operator action could clear — and because the shipped `InvoiceDocumentTypeMap.DirectionFor` classifies `CreditNote` as `AP` (`src/Interfaces/Invoices/Finance.Invoices.API/Services/InvoiceDocumentTypeMap.cs`, lines 45-51) while the note itself moves the CUSTOMER control account, that phantom balance would surface as a **payable** and drive the reported AP total permanently away from the GL. SDD-PAY-002 §2.3 therefore skips the open-item row for such a document type at the source; §2.1 states the matching inclusion predicate here, driven by the SAME table so the two can never drift. Credit-note settlement (refund, or offset against the document it corrects) is DEFERRED with the refund feature (§5).

**Sub-ledger, not general ledger.** The totals here are the AR/AP **sub-ledger** view. On a consistent books the total `AR` outstanding corresponds to the GL customers control account (`411`) balance and the total `AP` outstanding to the suppliers control account (`401`), both readable from SDD-FIN-003. v1 does not assert that reconciliation — it would require a cross-service read the Payments service does not have — and the reconciliation report is deferred to SDD-RPT-* (§7). That correspondence is moreover a **base-currency-only** expectation: the general ledger is currently currency-naive (the Posting Engine pins every line's rate to `1.000000` and writes the transactional amount into the base-amount columns for every document type, invoices included), so for a foreign-currency document the GL control-account balance is NOT the booking-rate base figure this spec reports. That is a pre-existing SDD-FIN-006 limitation which this spec neither introduces nor fixes — see the §7 open item.

**ISA-95 classification.** AP/AR aging and counterparty balances are ISA-95 **Level 4 (Business Planning & Logistics)** financial **reporting** views (ISA-95 / IEC 62264 Part 1, §5 — Business Planning & Logistics). They are read-only projections over the Level-4 business transactions and their sub-ledger matching records (`Invoice` — SDD-INV-001; `Payment` — SDD-PAY-001; `PaymentAllocation` and `InvoiceOpenItem` — SDD-PAY-002); they create no new business-transaction records and change no state. Because there is **no state change**, no immutable domain event and no audit row is required (the immutable-event/audit obligation in SDD-INFRA-006 / SDD-AUDIT-001 applies to the state-changing operations owned by SDD-PAY-001/-002, not to read queries). No Level-3 (MES) production activity is modelled. The counterparty (customer/supplier) is Warehouse-owned Level-4 master data referenced by GUID, NOT a foreign key (cross-service joins are forbidden, Plan §8).

**Scope — covered (v1):**
- `GET /api/v1/open-items` — filtered/paged list of open items (`direction`, `counterpartyId`, `currencyCode`, `overdueOnly`, `asOfDate`), each with `Outstanding`, `BaseOutstanding`, `DaysPastDue`, and its bucket label.
- `GET /api/v1/aging` — bucketed aging report for one `direction` as of `asOfDate`, optionally narrowed by `counterpartyId` / `currencyCode`, with per-counterparty rows, per-bucket amounts, and report-level bucket totals.
- `GET /api/v1/counterparty-balances` — paged one-row-per-(counterparty, currency) outstanding + overdue summary for one `direction` as of `asOfDate`.
- The aging bucket definition (`Current`, `1-30`, `31-60`, `61-90`, `90+` as documented defaults) and its per-request configurability via ascending day boundaries.
- As-of-date semantics: which items are in scope, how days-past-due is derived, and how a historical `asOfDate` derives its settled amount from the allocation rows (§2.3).
- The outstanding formula `GrossTotal − SettledAmount`, the eligible-status set `{ Confirmed, Posted }`, and the settleable-document-type population `{ SaleInvoice, PurchaseInvoice, DebitNote }` (`CreditNote` excluded — §2.1).
- Dual-currency reporting: every amount is reported in both the item's transactional currency and the base currency.
- Deterministic paging through SDD-INFRA-005 with the projection key appended as the final sort term; PageSize cap 200.
- The explicit no-caching rule (SDD-INFRA-004) and the new `finance.aging:read` permission.

**Scope — excluded (deferred):**
- **Everything that writes** — recording, confirming, posting, cancelling, or reversing a payment is SDD-PAY-001; allocating/deallocating and maintaining `InvoiceOpenItem.SettledAmount` is SDD-PAY-002. This spec never mutates a row.
- **The `InvoiceOpenItem` projection itself** — its columns, its event-fed consumers, its idempotency, and its eventual-consistency guarantees are SDD-PAY-002. This spec reads it and inherits those properties; it does NOT redefine them.
- **Counterparty statements** (a per-counterparty document listing invoices, payments, and a running balance for a period) — Phase 7, SDD-RPT-*. This spec is the raw aggregation those statements read.
- **Materialized / indexed aging views** — v1 aggregates live on read, exactly as SDD-FIN-003 does; a maintained snapshot table is a future `CHG-ENH-*` that MUST reproduce identical results.
- **Credit limits, credit blocking, dunning / reminder letters, and interest on overdue balances** — a later batch (`CHG-FEAT-*`); this spec exposes the numbers those features would consume.
- **Unallocated-payment (on-account cash) aging** — a `Confirmed`/`Posted` payment with `UnallocatedAmount > 0` is NOT netted into any counterparty balance in v1; the aging view is invoice-only (§2.10, §7).
- **Credit notes** — excluded from every open item, bucket and total in v1 because no `SettlementPairing` entry can settle one (§2.1). Credit-note settlement (refund, or offset against the invoice it corrects via the invoice-side `Invoice.CorrectsInvoiceId`) is deferred with the refund/offset feature, which SDD-PAY-002 §5/§7 owns; when it lands, the `SettlementPairing` widening brings credit notes into this aged population without a change to the aggregation semantics defined here (§5).
- **Counterparty name / address enrichment** — the counterparty is Warehouse-owned master data; v1 returns the GUID only. Enrichment is SDD-INT-WH-002 (not yet authored).
- **FX revaluation of outstanding balances** at period end (unrealized FX on open items) — SDD-FIN-005. v1 converts with the frozen `BookingExchangeRate` only (§2.2).
- **Formatted / exported reporting and НАП output** — the future SDD-RPT-* / SDD-INT-NAP-* specs (none yet authored).

## 2. Behavior

> **Service/controller contract (SDD-INFRA-009).** `AgingService` (`src/Interfaces/Payments/Finance.Payments.API/Services/AgingService.cs`, contract `Interfaces/IAgingService.cs`) MUST return `Result` / `Result<T>` — never `null`, never a thrown exception for a business outcome. The open-item list MAY inherit `SearchableServiceBase<InvoiceOpenItem, OpenItemDto, PaymentsDbContext>` for the SDD-INFRA-005 `FilterRequest`→`PagedResult` mechanics or compose the filter pipeline directly; either way the read path MUST honor the PageSize cap (200) and the deterministic ordering of §2.5/§2.7. `OpenItemsController`, `AgingController`, and `CounterpartyBalancesController` each inherit `BaseApiController` and translate every result via `ToActionResult(...)`. The service MUST NOT inject `IWorkflowEngine<T>`, `IAuditService`, `IPublishEndpoint`, `ISequenceGenerator`, or `ICacheService<T>` — it changes no state, writes no audit row, publishes no event, allocates no number, and caches nothing. `CancellationToken` MUST be threaded controller → service → query.

### 2.1 Aggregation source & inclusion rule (MUST — SDD-PAY-002)
- Every endpoint MUST read from the local `InvoiceOpenItem` projection in the `payments` schema (SDD-PAY-002), joined where §2.3 requires it to `payments.PaymentAllocations` and `payments.Payments`. It MUST NOT call the Invoices service, MUST NOT cross-database-join into `finance_invoices`, and MUST NOT introduce a new cross-service read client (Plan §8).
- An open item MUST be included only when its mirrored `InvoiceStatus` ∈ { `Confirmed`, `Posted` }. `Cancelled` and `Reversed` MUST BOTH be excluded from every row, bucket, counterparty total, and grand total — a cancelled document was voided before it ever bound the counterparty, and a reversed one has been fully offset by its correcting credit note (SDD-INV-001 §2.7), so neither carries an outstanding balance. `Draft` invoices never enter the projection (SDD-PAY-002), so no additional predicate is required for them, but the inclusion predicate MUST be written as the explicit positive set — never as `!= Cancelled` or `not in { Cancelled, Reversed }`.
- An open item MUST be included only when its `DocumentType` is settleable — i.e. when at least one SDD-PAY-002 `SettlementPairing` entry admits it. In v1 that set is exactly { `SaleInvoice`, `PurchaseInvoice`, `DebitNote` } and `CreditNote` MUST be excluded from every open-item row, bucket, counterparty total, and grand total: no payment document type can settle a credit note (`CustomerReceipt → { SaleInvoice, DebitNote }`, `SupplierPayment → { PurchaseInvoice }` — SDD-PAY-002 §2.5 rule 10), so its `Outstanding` could never reach `0.00` and it would age `1-30 → 90+` forever as a phantom `AP` payable (§1, §2.10).
  - The predicate MUST be evaluated through the SAME shared table SDD-PAY-002 owns (`src/Finance.Common/Settlement/SettlementPairing.cs` — its document-type-level settleability predicate, the one SDD-PAY-002 §2.3's skip rule uses), NEVER through a document-type list re-derived in this service, so widening or narrowing the pairing can never leave the aged population and the allocatable population disagreeing.
  - The predicate is **defense-in-depth**, not the primary enforcement: SDD-PAY-002 §2.3's `InvoiceConfirmedEvent` consumer already skips creating the row, so in a correct deployment no unsettleable open item exists to filter. It MUST still be applied here, because a row can predate the skip rule, arrive from the deferred reconciliation job (SDD-PAY-002 §7), or be introduced by a future pairing change — and because these totals feed the AP/AR control-account comparison, a single phantom row is a permanent divergence.
- An open item whose `Outstanding` (§2.2) is exactly `0.00` MUST be excluded from the open-item list, from every aging bucket, and from the counterparty balances. Fully settled documents are history, not open items.
- `Payment` rows are NEVER aged in v1: a `Confirmed`/`Posted` payment with an `UnallocatedAmount > 0` MUST NOT reduce any counterparty balance and MUST NOT appear as a negative open item (§2.10, deferred in §7).
- All monetary results MUST be `decimal` / `DECIMAL(18,2)` and all rates `decimal` / `DECIMAL(18,6)`; `double`/`float` MUST NOT appear anywhere in the computation (SDD-FIN-005 / SDD-INFRA-001).

### 2.2 Outstanding amount, dual-currency reporting & grouping key (MUST — SDD-CTRY-001 / SDD-FIN-005)
- For every open item, `Outstanding` MUST be `GrossTotal − SettledAmount` in the item's **transactional** currency (`CurrencyCode`), compared and reported to the cent.
- Every reported amount MUST be accompanied by its base-currency counterpart: `BaseOutstanding` MUST be `Outstanding × BookingExchangeRate` rounded through `ICountryStrategy.ApplyTaxRounding` (SDD-CTRY-001). The rate is the `BookingExchangeRate` frozen on the projection when the invoice was booked — this spec MUST NOT look up a current rate and MUST NOT revalue (deferred to SDD-FIN-005). That rate is a **real** booking rate, not a placeholder: SDD-PAY-002's consumer copies it from `InvoiceConfirmedEvent.BookingExchangeRate`, which SDD-INV-001 §2.14 sources from the invoice's own frozen `ExchangeRate DECIMAL(18,6)` column. `BaseOutstanding` is therefore trustworthy for a foreign-currency invoice — a `EUR` invoice on a `BGN` base MUST report its `BGN` counterpart at the booked cross rate — and this spec MUST NOT qualify it as an approximation.
- When `CurrencyCode == BaseCurrencyCode`, `BookingExchangeRate` is `1.000000` and `BaseOutstanding` MUST equal `Outstanding` exactly. The reporting base currency reported on the response MUST come from `ICountryStrategy.BaseCurrencyCode`; the per-item `BaseCurrencyCode` mirrored on the projection MUST be echoed unchanged.
- Because summing amounts across different transactional currencies is meaningless, every grouped row MUST be keyed by the pair (`CounterpartyId`, `CurrencyCode`). A counterparty holding open items in two currencies MUST therefore produce two rows. Only the base-currency column MAY be summed across rows, and report-level grand totals MUST be expressed in base currency only.
- `DaysPastDue` MUST be the whole number of days from `DueDate` to `asOfDate` computed on the **date** parts only (`asOfDate` − `DueDate`), so a same-day comparison yields `0` regardless of the time-of-day component of the `DATETIMEOFFSET` values. A not-yet-due item yields a value ≤ `0`.

### 2.3 As-of-date semantics (MUST)
- `asOfDate` MUST be interpreted as the inclusive upper bound of the accounting view: an open item is in scope only when its `IssueDate` date part is ≤ `asOfDate`. An item issued after `asOfDate` MUST be excluded even if it is unsettled.
- `asOfDate` MUST also be the reference date for `DaysPastDue` and therefore for bucket assignment (§2.4).
- `asOfDate` MUST NOT be in the future — a future date would age not-yet-due documents against a calendar that has not happened. A future value MUST be rejected with `INVALID_AGING_AS_OF_DATE` before any query runs. It is REQUIRED on `/aging` and `/counterparty-balances` and OPTIONAL on `/open-items` (defaulting to the current date).
- The settled amount used at `asOfDate` MUST be resolved by exactly one of two paths, and the choice MUST be driven solely by the date:
  - **Current-state path (asOfDate is today).** `SettledAmount` MUST be read straight from the `InvoiceOpenItem` projection column — it is the maintained authority (SDD-PAY-002) and needs no join.
  - **Historical path (asOfDate strictly before today).** The projection column is current-state only and therefore MUST NOT be used. The as-of settled amount MUST be derived as Σ `PaymentAllocation.AllocatedAmount` over the item's allocation rows whose `AllocatedAt` date part is ≤ `asOfDate` AND whose owning `Payment.Status` ∈ { `Confirmed`, `Posted` }. Allocations belonging to a `Draft`, `Cancelled`, or `Reversed` payment MUST be excluded.
- The two paths MUST agree: for `asOfDate` = today the allocation-derived sum MUST equal the projection's `SettledAmount` on a consistent sub-ledger. A test MUST assert this equality rather than assume it — a mismatch indicates a projection defect and MUST be surfaced by that test, not silently corrected at read time.
- Because a deallocation removes the allocation row (SDD-PAY-002), the historical path reports the sub-ledger **as it stands now, replayed by allocation date** — not a bi-temporal audit reconstruction. This limitation MUST be stated in the response documentation; full point-in-time reconstruction from the audit trail is deferred (§7).

### 2.4 Aging bucket definition & configurability (MUST)
- The documented default buckets MUST be exactly five, derived from the day boundaries `30, 60, 90`:
  - `Current` — `DaysPastDue ≤ 0` (not yet due, including due exactly on `asOfDate`).
  - `1-30` — `1 ≤ DaysPastDue ≤ 30`.
  - `31-60` — `31 ≤ DaysPastDue ≤ 60`.
  - `61-90` — `61 ≤ DaysPastDue ≤ 90`.
  - `90+` — `DaysPastDue > 90` (the open-ended final bucket).
- A caller MAY supply a `buckets` parameter as an ascending list of positive day boundaries (e.g. `15,30,60`), which MUST yield `Current`, `1-15`, `16-30`, `31-60`, `60+`. When the parameter is omitted the default `30,60,90` MUST be used.
- Supplied boundaries MUST be strictly ascending positive integers and MUST NOT exceed 6 values (7 buckets including `Current`); any violation MUST be rejected with `INVALID_AGING_BUCKETS` before any query runs.
- Every open item in scope MUST fall into exactly one bucket — the buckets MUST be exhaustive and mutually exclusive. Consequently, for every counterparty row, Σ (bucket `Outstanding`) MUST equal the row's `TotalOutstanding` to the cent, and Σ (bucket `BaseOutstanding`) MUST equal `TotalBaseOutstanding`.
- The response MUST echo both the effective numeric boundaries and the bucket labels in bucket order, and each bucket MUST carry its `FromDaysPastDue` / `ToDaysPastDue` (`null` on the open-ended final bucket), `Outstanding`, `BaseOutstanding`, and `ItemCount`. A client MUST NOT have to re-derive a label or a boundary to render the report.
- Bucket assignment MUST live in a pure, injectable calculator (`Services/AgingBucketCalculator.cs`, registered as a bare concrete class in the manner of `InvoiceTotalsCalculator`) so it is unit-testable without a database.

### 2.5 Open items — `GET /api/v1/open-items` (MUST)
- The endpoint MUST accept a `FilterRequest` (SDD-INFRA-005) plus the query narrowings `direction` (`AR`|`AP`, optional), `counterpartyId` (optional), `currencyCode` (optional), `overdueOnly` (optional, default `false`), and `asOfDate` (optional, default the current date), and MUST return `PagedResult<OpenItemDto>`.
- `OpenItemDto` is declared by **this** spec in `src/Finance.ServiceModel/Payments/OpenItemDto.cs` and is owned here, because it carries the computed report fields `Outstanding`, `BaseOutstanding`, `DaysPastDue`, and `AgingBucket` that only this aggregation produces. SDD-PAY-002 declares no projection-mirror DTO — its list endpoint returns `PaymentAllocationDto` only — so there MUST be exactly one open-item shape in the solution (§7).
- Each `OpenItemDto` MUST carry `InvoiceId`, `DocumentNumber`, `DocumentType`, `Direction`, `CounterpartyId`, `CurrencyCode`, `BaseCurrencyCode`, `GrossTotal`, `SettledAmount`, `Outstanding`, `BaseOutstanding`, `IssueDate`, `DueDate`, `DaysPastDue`, `AgingBucket` (the label from §2.4), `SettlementStatus` (SDD-PAY-002), and `InvoiceStatus`.
- `overdueOnly = true` MUST return only items whose `DaysPastDue ≥ 1`; `overdueOnly = false` MUST return both current and overdue items.
- The list MUST run through `IQueryable<InvoiceOpenItem>.ApplyFilter(request)` (SDD-INFRA-005), MUST cap `PageSize` at 200, and MUST be ordered by `DueDate` ascending then the projection key `InvoiceId` (the PK appended as the final deterministic sort term so pagination is stable). Oldest-due-first is REQUIRED so the list reads as a collection worklist.
- The filterable/sortable surface MUST be opt-in via `[Filterable]`/`[Sortable]` on `InvoiceOpenItem` (declared by SDD-PAY-002): `DocumentNumber`, `DocumentType`, `Direction`, `CounterpartyId`, `CurrencyCode`, `IssueDate`, `DueDate`, `InvoiceStatus`.
- Requires permission `finance.payment:read` — the same permission that reads payments, because an open item is a projection the Payments service already owns (§2.9).

### 2.6 Aging — `GET /api/v1/aging` (MUST)
- The endpoint MUST accept `asOfDate` (required), `direction` (required, `AR` or `AP`), `counterpartyId` (optional), `currencyCode` (optional), and `buckets` (optional, §2.4), and MUST return a single `AgingReportDto`.
- The report MUST carry the effective `AsOfDate`, `Direction`, reporting `BaseCurrencyCode`, the effective bucket boundaries and labels, the per-counterparty `Rows`, and the report-level per-bucket `Totals` expressed in base currency.
- Each row MUST be keyed by (`CounterpartyId`, `CurrencyCode`) per §2.2 and MUST carry its bucket breakdown, `TotalOutstanding` (transactional), and `TotalBaseOutstanding`.
- A counterparty whose in-scope outstanding is `0.00` MUST be omitted from the report entirely — an all-zero row MUST NOT be emitted (§2.10).
- Rows MUST be ordered deterministically by `TotalBaseOutstanding` descending, then `CounterpartyId`, then `CurrencyCode` (the composite grouping key is the final sort term because a grouped row has no entity PK — see §7).
- The aging report MUST be a single grouped round-trip: the implementation MUST NOT issue one query per counterparty or one query per bucket (no N+1 over the grouping key).
- The report MUST NOT be cached (§2.8). Requires permission `finance.aging:read` (§2.9).

### 2.7 Counterparty balances — `GET /api/v1/counterparty-balances` (MUST)
- The endpoint MUST accept `asOfDate` (required), `direction` (required), `currencyCode` (optional), and a `FilterRequest` for paging/sorting, and MUST return `PagedResult<CounterpartyBalanceDto>`.
- Each `CounterpartyBalanceDto` MUST carry `CounterpartyId`, `CurrencyCode`, `BaseCurrencyCode`, `Direction`, `OpenItemCount`, `Outstanding`, `BaseOutstanding`, `OverdueOutstanding`, `BaseOverdueOutstanding`, and `OldestDueDate` (the earliest `DueDate` among the counterparty's in-scope open items, `null` when there are none).
- `OverdueOutstanding` MUST be the subset of `Outstanding` whose items have `DaysPastDue ≥ 1`, i.e. the sum of every non-`Current` bucket for that row. For any (counterparty, currency) pair the `/aging` and `/counterparty-balances` endpoints MUST report the same `TotalOutstanding` for the same `asOfDate` and `direction` — the two endpoints MUST share one aggregation path so they cannot drift.
- A counterparty with zero outstanding MUST be omitted from the page and MUST NOT be counted in `TotalCount`.
- The page MUST cap `PageSize` at 200 and MUST be ordered by `BaseOutstanding` descending then (`CounterpartyId`, `CurrencyCode`) as the final deterministic term.
- The balances MUST NOT be cached (§2.8). Requires permission `finance.aging:read` (§2.9).

### 2.8 Projection freshness & no caching (MUST — SDD-PAY-002 / SDD-INFRA-004)
- Open items, aging, and counterparty balances MUST NOT be cached. They are derived from transactional data (invoices, allocations, payments), which SDD-INFRA-004 forbids caching. Every request MUST recompute from the current projection state; no `ICacheService<T>` MUST be injected into the read path, and the `finance-payments` prefix MUST NOT be added to `FinanceCacheOptions.RegisteredServicePrefixes` for this spec.
- The read path is **eventually consistent** by construction: the `InvoiceOpenItem` projection is fed by the idempotent MassTransit consumers of the invoice confirmed/posted/cancelled/reversed events (SDD-PAY-002 §2.3). An invoice confirmed moments ago MAY be absent from the aging report until its event is consumed; a cancelled or reversed invoice MAY still appear until its own event is consumed. Lag is the ONLY reason a settleable document is temporarily absent — a confirmed `CreditNote` is absent permanently and by design (§2.1), not pending consumption, and MUST NOT be treated as projection lag or as a defect to repair. This MUST be documented on the endpoints and MUST NOT be worked around by reading the Invoices service synchronously.
- Because the projection is the only source, aging is unaffected by fiscal-period status: a `Closed` period's invoices are still aged (period lifecycle is SDD-FIN-004; read queries are period-status-agnostic, mirroring SDD-FIN-003).

### 2.9 Cross-cutting obligations (MUST)
- `/open-items` MUST be protected by `[RequirePermission("finance.payment:read")]`. `/aging` and `/counterparty-balances` MUST be protected by `[RequirePermission("finance.aging:read")]` — a distinct report-level permission, so a collections/finance-reporting role can be granted the roll-ups without being granted the individual payment records. Both are decoded via the shared `Warehouse.Auth.Shared` package (SDD-INT-AUTH-001).
- `finance.aging:read` is a NEW permission. Because SDD-INT-AUTH-001's permission auto-registration is still DEFERRED, it MUST be seeded **manually** in the auth-service (and granted to the finance-reporting role) as part of this batch's deployment steps; the endpoints will otherwise return `403` for every caller. This obligation MUST be recorded in the change spec, not only in code — and it now is: **`CHG-ENH-003`** (`docs/changes/CHG-ENH-003-payments-rbac-permission-seeding.md`) enumerates all eight new Payments permission strings — the seven `finance.payment:*` ones SDD-PAY-001/-002 add plus `finance.aging:read` — against the `[RequirePermission(...)]` attributes actually shipped on the controllers, together with the failure mode and the suggested role grants. The obligation to RECORD the seeding is therefore discharged; the seeding ACTION is NOT, and no artifact in this repository can discharge it (the auth-service owns the permission store). Until an operator performs the `CHG-ENH-003` steps, `GET /api/v1/aging` and `GET /api/v1/counterparty-balances` return `403` for every caller, read-only reporting roles included. Shipped attributes, verified: `AgingController.cs:61` and `CounterpartyBalancesController.cs:57` declare `finance.aging:read`, `OpenItemsController.cs:57` declares `finance.payment:read`, and `PaymentControllerContractTests.AgingControllers_DeclareTheReportLevelAgingPermission_ButOpenItemsReadsAsAPayment` pins all three.
- `CorrelationId` MUST flow via `ICorrelationIdAccessor` / `CorrelationIdMiddleware` (SDD-INFRA-001); the endpoints MUST be traced via OpenTelemetry with the `correlation_id` Activity tag and MUST use NLog structured templates — no string interpolation in log calls (SDD-OBS-001).
- Counterparty GUIDs MUST NOT be logged in full and MUST NOT appear in a log message body (SDD-OBS-001 sensitive-field rule); they are returned in the response payload only.
- The three routes MUST be exposed through the gateway (`src/Infrastructure/Gateway/Finance.Gateway/appsettings.json.template` — `open-items-route`, `aging-route`, `counterparty-balances-route`, all pointing at the `payments-cluster` that SDD-PAY-001 adds).
- Every action MUST declare one `[ProducesResponseType]` per documented outcome and take `CancellationToken` last. The outcome sets are: `GET /open-items` → 200/400/403; `GET /aging` → 200/400/403; `GET /counterparty-balances` → 200/400/403. All three are read endpoints with no 404 and no 409 (§4) — an unknown counterparty or an empty window is a `200` with an empty payload (§2.10), and `403` comes from the RBAC layer, not from a domain code.
- `CancellationToken` MUST be threaded controller → service → query.

### 2.10 Edge cases (MUST)
- **Empty result is not an error.** A `direction`/`counterpartyId`/`currencyCode` combination with no in-scope open items MUST return a well-formed empty response (empty `Rows`/`Items`, zero bucket totals) with `200` — never a `404`.
- **Unknown counterparty.** A `counterpartyId` that exists in no open item MUST yield an empty report with `200`. Counterparty existence MUST NOT be pre-checked: the counterparty is Warehouse-owned master data and this service has no read client for it (deferred to SDD-INT-WH-002). There is deliberately no `COUNTERPARTY_NOT_FOUND` code.
- **Fully settled invoice disappears.** An invoice whose allocations bring `SettledAmount` to `GrossTotal` MUST vanish from the open-item list, from every bucket, and from the counterparty's `OpenItemCount` — while remaining visible in the SDD-PAY-002 allocation views. Deallocating MUST make it reappear.
- **Cancelled invoice excluded after the event lands.** Once the cancellation event has been consumed and the projection's `InvoiceStatus` is `Cancelled`, the item MUST be excluded from every total; before that it MAY still appear (§2.8).
- **Reversed invoice excluded after the event lands.** Once the `InvoiceReversedEvent` has been consumed and the projection's `InvoiceStatus` is `Reversed`, the item MUST be excluded from every open-item row, bucket, counterparty total, and grand total — the correcting credit note offset it in full (SDD-INV-001 §2.7), so it has no outstanding balance; before that it MAY still be aged as `Posted` (§2.8).
- **A confirmed credit note is never aged.** Trace the full sequence: a `200.00` `CreditNote` is issued to customer C and confirmed; SDD-INV-001 publishes `InvoiceConfirmedEvent` for it exactly as for the other three document types; SDD-PAY-002 §2.3's consumer skips the open-item row because no `SettlementPairing` entry admits `CreditNote`; and even if a row existed (a pre-skip row, or one repaired by the deferred reconciliation job), §2.1's settleable-document-type predicate MUST exclude it. The note MUST therefore appear in NO `/open-items` page, NO `/aging` bucket for either `direction`, and NO `/counterparty-balances` row — in particular it MUST NOT be reported by `direction=AP` (which is where the shipped `InvoiceDocumentTypeMap.DirectionFor` classification would otherwise put it) as a `200.00` payable that ages `1-30 → 90+` forever while the AR sub-ledger reads `0.00` against a GL `411` of `−200.00`. Neither of §2.1's other exits could ever clear it: `Outstanding` cannot reach `0.00` (no payment can settle it) and its `InvoiceStatus` stays `Confirmed`/`Posted` (it is a valid, in-force document, not a cancelled or reversed one). Excluding it is what keeps both control accounts from diverging permanently.
- **Due exactly on `asOfDate`.** An item whose `DueDate` date part equals `asOfDate` MUST have `DaysPastDue == 0` and MUST land in `Current` — never in `1-30`. An item due one day earlier MUST land in `1-30`.
- **Bucket boundary days.** `DaysPastDue == 30` MUST land in `1-30` and `31` in `31-60`; `90` MUST land in `61-90` and `91` in `90+` (the final bucket is strictly greater than the last boundary).
- **Issued after `asOfDate`.** An unsettled invoice whose `IssueDate` is after `asOfDate` MUST be excluded entirely, including from `OpenItemCount` and `OldestDueDate`.
- **Historical as-of ignores later payments.** An invoice settled today MUST still show its full `Outstanding` for an `asOfDate` before the allocation's `AllocatedAt`, via the §2.3 historical path; an allocation belonging to a `Cancelled` payment MUST NOT reduce the outstanding at any `asOfDate`.
- **Multi-currency counterparty.** A counterparty with open items in `BGN` and `EUR` MUST produce two rows (one per currency); their `BaseOutstanding` values MAY be summed by the caller, but no cross-currency transactional total MUST ever be emitted.
- **Unallocated cash is invisible.** A counterparty whose only activity is a `Confirmed` payment with `UnallocatedAmount > 0` and no open invoice MUST be omitted entirely (zero outstanding) — the balance MUST NOT go negative in v1 (§7).
- **Future `asOfDate`.** A request whose `asOfDate` is after the current date MUST be rejected with `INVALID_AGING_AS_OF_DATE` (400) before any query runs.

## 3. Validation Rules

All validation here is shape-only and stays in FluentValidation; no `IChainValidator` chain is used because no rule depends on other rows or on aggregate state (SDD-INFRA-007 does not apply). The §3.2 entries are computed read-path assertions over the aggregation output, not registered validators.

### 3.1 Field-level (FluentValidation — codes in `PaymentErrorCodes`)

| Endpoint | Field | Rule | Error code |
|---|---|---|---|
| Aging / Counterparty Balances | `asOfDate` | Required; MUST NOT be in the future | `INVALID_AGING_AS_OF_DATE` |
| Open Items | `asOfDate` (optional) | When present, MUST NOT be in the future; defaults to the current date | `INVALID_AGING_AS_OF_DATE` |
| Aging / Counterparty Balances | `direction` | Required; `AR` or `AP` | `INVALID_AGING_DIRECTION` |
| Open Items | `direction` (optional) | When present, `AR` or `AP` | `INVALID_AGING_DIRECTION` |
| Aging | `buckets` (optional) | Strictly ascending positive integers, ≤ 6 boundaries; defaults to `30,60,90` | `INVALID_AGING_BUCKETS` |
| All | `counterpartyId` (optional) | When present, a non-empty GUID | `INVALID_COUNTERPARTY_ID` |
| All | `currencyCode` (optional) | When present, a 3-letter ISO 4217 code | `INVALID_AGING_CURRENCY` |
| All | `FilterRequest.PageSize` | ≤ 200 (cap enforced by SDD-INFRA-005) | `PAGE_SIZE_TOO_LARGE` |

### 3.2 Cross-field / computed (read-path assertions)

| Rule | Mechanism | Surfaced as |
|---|---|---|
| `Outstanding == GrossTotal − SettledAmount` per open item | computed over the SDD-PAY-002 projection columns | reported `Outstanding`; `0.00` ⇒ the item is omitted (§2.1) |
| `BaseOutstanding == ApplyTaxRounding(Outstanding × BookingExchangeRate)` | computed via `ICountryStrategy` (SDD-CTRY-001) | reported `BaseOutstanding`; equals `Outstanding` when the item is in base currency |
| `Σ bucket Outstanding == row TotalOutstanding` (and the same in base) | buckets are exhaustive + mutually exclusive (§2.4) | response consistency (asserted by test) |
| `OverdueOutstanding == TotalOutstanding − Current bucket` | one shared aggregation path for `/aging` and `/counterparty-balances` (§2.7) | response consistency across the two endpoints (asserted by test) |
| Allocation-derived settled == projection `SettledAmount` at `asOfDate` = today | §2.3 dual path | test-asserted equality; a mismatch is a projection defect, not read-time repair |
| Only `Confirmed`/`Posted` items aggregated; `Cancelled` and `Reversed` excluded | query predicate (`InvoiceStatus ∈ { Confirmed, Posted }`) | totals reflect in-force documents only |
| Only settleable document types aggregated; `CreditNote` excluded | query predicate over the shared `SettlementPairing` settleability check (§2.1); primary enforcement is SDD-PAY-002 §2.3's skip | totals reflect only documents a payment can discharge — no phantom, never-clearable balance |

### 3.3 State-based

| Condition | Rule | Outcome |
|---|---|---|
| Counterparty has no in-scope open items | Omit from aging / balances | `200`, omitted (§2.6, §2.7) — not an error |
| `counterpartyId` unknown to the projection | No existence pre-check; empty report | `200`, empty (§2.10) — not an error |
| Open item fully settled (`Outstanding == 0.00`) | Exclude from list, buckets, and counts | `200`, absent (§2.1) — not an error |
| Open item's `DocumentType` is not settleable (`CreditNote`) | Exclude from list, buckets, counts, and totals | `200`, absent (§2.1, §2.10) — not an error |
| Invoice event not yet consumed by the projection | Item absent (or stale) until consumed | `200`, eventually consistent (§2.8) — not an error |
| Payment has `UnallocatedAmount > 0` and no open invoice | Ignore; balance stays zero, never negative | `200`, omitted (§2.10) — v1 by design |
| `asOfDate` in the future | Reject before query | `INVALID_AGING_AS_OF_DATE` (400) |
| `direction` missing on `/aging` or `/counterparty-balances` | Reject before query | `INVALID_AGING_DIRECTION` (400) |

## 4. Error Rules

All errors are RFC-7807 ProblemDetails per SDD-INFRA-001 (`title` = code, `detail` = developer English, `type` = `https://finance.local/errors/{code}`). `BaseApiController.ToActionResult` maps codes to HTTP via `IErrorCodeToStatusMap` (SDD-INFRA-009); services return `Result.Failure(code, detail)`. Constants live in `Finance.Common/ErrorCodes/PaymentErrorCodes.cs` (SCREAMING_SNAKE_CASE) — the file SDD-PAY-001 creates; the codes below are ADDITIVE to it and MUST NOT be raw string literals in any `.WithErrorCode(...)` call. This is a read API, so the error surface is intentionally minimal and contains no 404 and no 409.

| Code | HTTP | Trigger | Type |
|---|---|---|---|
| `INVALID_AGING_AS_OF_DATE` | 400 | `asOfDate` missing on `/aging` or `/counterparty-balances`, or in the future on any endpoint | Validation (range) |
| `INVALID_AGING_DIRECTION` | 400 | `direction` missing on a report endpoint, or not `AR`/`AP` | Validation (shape) |
| `INVALID_AGING_BUCKETS` | 400 | `buckets` not strictly ascending, non-positive, or more than 6 boundaries | Validation (shape) |
| `INVALID_COUNTERPARTY_ID` | 400 | `counterpartyId` supplied as an empty GUID | Validation (shape) |
| `INVALID_AGING_CURRENCY` | 400 | `currencyCode` supplied but not a 3-letter ISO 4217 code | Validation (shape) |
| `PAGE_SIZE_TOO_LARGE` | 400 | `FilterRequest.PageSize` exceeds 200 (from SDD-INFRA-005) | Validation (paging) |

Every code above is a 400 and therefore needs no `IErrorCodeToStatusMap` extension: `DefaultErrorCodeToStatusMap` already falls through to 400 for codes that match none of the `*_NOT_FOUND` / `*_CONFLICT` / `CONCURRENT_*` patterns, and the `PaymentErrorCodeToStatusMap` that SDD-PAY-001 registers MUST leave them alone. `PAGE_SIZE_TOO_LARGE` lives in `FilterErrorCodes` (SDD-INFRA-005, reused — NOT redefined).

There is deliberately no `COUNTERPARTY_NOT_FOUND` and no `OPEN_ITEM_NOT_FOUND`: an unknown counterparty and an empty window are valid, common business states that MUST return an empty `200` (§2.10), mirroring the empty-ledger default of SDD-FIN-003 §2.4. A request that a caller is not permitted to make returns `403` from the RBAC layer (SDD-INT-AUTH-001), not a domain code.

**Frontend locale keys — SHIPPED in this batch, ahead of any aging view.** Every code above MUST have a matching `errors.<CODE>` entry in BOTH `frontend/src/shared/i18n/locales/en.ts` and `bg.ts` (SDD-UI-001, CLAUDE.md §0.3.B), and all of them now do: the five aging codes landed in THIS batch at `en.ts` / `bg.ts` lines 498-502 rather than waiting for the AP/AR aging frontend's PR, and `PAGE_SIZE_TOO_LARGE` was already present from SDD-INFRA-005 (`en.ts` / `bg.ts` line 387). So a `400` from any of the three endpoints already renders a translated message through `getApiErrorMessage`, not a raw key path. The covering test is `frontend/src/shared/i18n/paymentErrorCodes.test.ts`, which asserts each aging code resolves in EN and in BG, that the whole `errors` group is at exact EN/BG parity, that no message is its own key path, and that the BG text is actually Cyrillic. Still deferred to the AP/AR aging frontend (a future `SDD-UI-FIN-002`, not yet authored): the aging view's column headers and its rendering of the `Current` label. The bucket labels (`Current`, `1-30`, …) are data, not translation keys (§2.4, §7), so they need `en`/`bg` entries only when a view renders them.

## 5. Versioning Notes

`/api/v1/open-items`, `/api/v1/aging`, and `/api/v1/counterparty-balances` are the v1 read surface.

- **v1 — Initial specification (Batch 17 — Phase 5, Payments).** Read-only aggregation inside `Finance.Payments.API` over the SDD-PAY-002 `InvoiceOpenItem` projection (+ `PaymentAllocation` + `Payment` for the historical as-of path). `Outstanding = GrossTotal − SettledAmount`; eligible statuses `{ Confirmed, Posted }` (`Cancelled` and `Reversed` excluded, the latter relying on the `InvoiceReversedEvent` + fourth projection consumer that SDD-INV-001 §2.7 / SDD-PAY-002 §2.3 shipped in this batch); the aged population is exactly the settleable document types `{ SaleInvoice, PurchaseInvoice, DebitNote }` per the shared `SettlementPairing` table, with `CreditNote` excluded at the source by SDD-PAY-002 §2.3's skip and by §2.1's mirroring predicate here; zero-outstanding items and counterparties omitted; five default buckets from the boundaries `30,60,90` with per-request configurability; dual-currency reporting with the frozen `BookingExchangeRate` and `ICountryStrategy.ApplyTaxRounding`; grouped rows keyed by (`CounterpartyId`, `CurrencyCode`); SDD-INFRA-005 filtering/paging with the key appended as the final sort term and a PageSize cap of 200; no caching; `finance.payment:read` for open items and the new `finance.aging:read` for the two reports.
- **No new persistence or messaging surface.** This spec adds no table, no migration, no event, no consumer, no audit row, no workflow state, and no enum — it is purely additive read surface on top of SDD-PAY-001/-002. Adding it therefore cannot break either sibling spec.
- **Deferred (future versions / specs):**
  - **Materialized / indexed aging views** — an allocation-event-fed outstanding snapshot (or an indexed view) for performance is a future `CHG-ENH-*` that MUST reproduce identical results to this live aggregation (mirrors the SDD-FIN-003 materialization deferral). Additive.
  - **Counterparty statements** — a per-counterparty period document with invoices, payments, and a running balance — Phase 7, SDD-RPT-* (a separate endpoint/spec that reads this aggregation; not a change to this surface).
  - **Credit limits / credit blocking / dunning / overdue interest** — a future `CHG-FEAT-*`; would consume `OverdueOutstanding` and `OldestDueDate` without changing them. Additive.
  - **Unallocated-payment (on-account cash) aging** — netting a payment's `UnallocatedAmount` into the counterparty balance would change the meaning of `Outstanding` and is therefore breaking; it requires `/api/v2/` plus a `CHG-ENH-*` (or an opt-in `includeUnallocatedCash` flag, which would be additive).
  - **Counterparty name/address enrichment** — SDD-INT-WH-002 (additive response fields).
  - **FX revaluation of open items** at period end — SDD-FIN-005 (additive `RevaluedBaseOutstanding` field; the booking-rate conversion defined here MUST remain available unchanged).
  - **Bi-temporal point-in-time aging** reconstructed from the audit trail rather than replayed by allocation date (§2.3) — a later batch.
  - **Credit-note aging and settlement** — deferred with the refund / offset feature that SDD-PAY-002 §5/§7 owns (it needs the accountant decision on the `CREDIT_NOTE` control account, SDD-PAY-001 §2.13/§7). Because §2.1's population predicate reads the shared `SettlementPairing` table rather than a local list, WIDENING that table to admit `CreditNote` brings credit notes into every open-item page, bucket, and counterparty total automatically — additive here, with no change to the formula, the buckets, or the grouping key. Offsetting a credit note against the document it corrects would additionally require the invoice-side `Invoice.CorrectsInvoiceId` link (`src/Databases/Finance.Invoices.DBModel/Models/Invoice.cs`) to be mirrored onto the projection, which SDD-PAY-002 §2.2's column set does not carry today — an additive projection column owned by that spec, not by this one.
- Adding a response field (counterparty name, an extra bucket column, a revalued base amount) or an optional query narrowing is additive (non-breaking). Changing the aggregation semantics — the outstanding formula, the eligible-status set, the default bucket boundaries, the grouping key, or netting unallocated cash — is breaking and requires `/api/v2/` plus a `CHG-ENH-*`. The settleable-document-type population follows `SettlementPairing`, so it inherits that table's own rule (SDD-PAY-002 §5): WIDENING it is additive, NARROWING it is breaking, because a document type that was aged stops being aged and every historical total for that counterparty changes.

## 6. Test Plan

> Environment: Docker/SQL/Redis/RabbitMQ are not available offline — only `[Unit]` tests run by default. EF unit tests use SQLite in-memory (`Fixtures/SqlitePaymentsDbContextFactory.cs`, mirroring the Invoices fixture set); the aggregation is a pure `SELECT … GROUP BY` over seeded `InvoiceOpenItem` / `PaymentAllocation` / `Payment` rows and needs no real broker, no outbox, and no workflow engine. Base-currency rounding uses a faked `ICountryStrategy`; bucket assignment is tested directly against `AgingBucketCalculator` with no database at all. `WebApplicationFactory` HTTP tests and real-SQL aggregation tests carry `[Category("Integration")]` and are excluded from the default run. All business tests MUST reference `[Category("SDD-PAY-003")]`.

### 6.1 Open-item aggregation & inclusion rule (Unit — SQLite in-memory)

| Test name | Kind |
|---|---|
| `OpenItems_Outstanding_EqualsGrossTotalMinusSettledAmount` | [Unit] |
| `OpenItems_IncludesConfirmedAndPostedOnly` | [Unit] |
| `OpenItems_ExcludesCancelledInvoices_FromEveryTotal` | [Unit] |
| `OpenItems_ExcludesReversedInvoices_FromEveryTotal` | [Unit] |
| `OpenItems_ExcludesCreditNotes_NoPaymentTypeCanSettleThem` | [Unit] |
| `OpenItems_FullySettledItem_Omitted_ZeroOutstanding` | [Unit] |
| `OpenItems_DeallocatedItem_ReappearsWithOutstanding` | [Unit] |
| `OpenItems_FiltersByDirection_ArExcludesAp` | [Unit] |
| `OpenItems_FiltersByCounterpartyId` | [Unit] |
| `OpenItems_FiltersByCurrencyCode` | [Unit] |
| `OpenItems_OverdueOnly_ExcludesNotYetDueItems` | [Unit] |
| `OpenItems_OrderedByDueDateThenInvoiceId_Deterministic` | [Unit] |
| `OpenItems_RespectsPageSizeCap_200` | [Unit] |
| `OpenItems_UnallocatedPayment_ProducesNoNegativeItem` | [Unit] |

### 6.2 Aging bucket assignment (Unit — pure `AgingBucketCalculator`, no database)

| Test name | Kind |
|---|---|
| `Buckets_DefaultBoundaries_ProduceCurrent1To30_31To60_61To90_90Plus` | [Unit] |
| `Buckets_DueDateEqualsAsOfDate_AssignedToCurrent` | [Unit] |
| `Buckets_NotYetDueItem_AssignedToCurrent_NegativeDaysPastDue` | [Unit] |
| `Buckets_OneDayPastDue_AssignedTo1To30` | [Unit] |
| `Buckets_ThirtyDaysPastDue_AssignedTo1To30_ThirtyOne_AssignedTo31To60` | [Unit] |
| `Buckets_NinetyDaysPastDue_AssignedTo61To90_NinetyOne_AssignedTo90Plus` | [Unit] |
| `Buckets_CustomBoundaries_ProduceRequestedBucketSet` | [Unit] |
| `Buckets_AreExhaustiveAndMutuallyExclusive_EveryItemInExactlyOne` | [Unit] |
| `Buckets_NonAscendingBoundaries_ReturnsInvalidAgingBuckets` | [Unit] |
| `Buckets_NonPositiveBoundary_ReturnsInvalidAgingBuckets` | [Unit] |
| `Buckets_MoreThanSixBoundaries_ReturnsInvalidAgingBuckets` | [Unit] |
| `Buckets_DaysPastDue_ComputedOnDatePartsOnly_IgnoresTimeOfDay` | [Unit] |

### 6.3 Aging report & counterparty balances (Unit — SQLite in-memory)

| Test name | Kind |
|---|---|
| `Aging_SumOfBucketOutstanding_EqualsRowTotalOutstanding` | [Unit] |
| `Aging_ZeroOutstandingCounterparty_IsOmitted` | [Unit] |
| `Aging_GroupsByCounterpartyAndCurrency_OneRowPerPair` | [Unit] |
| `Aging_ArAndApSeparated_ByDirection` | [Unit] |
| `Aging_ConfirmedCreditNote_IsNotAged` | [Unit] |
| `Aging_EchoesEffectiveBucketBoundariesAndLabels` | [Unit] |
| `Aging_RowsOrderedByBaseOutstandingDescThenGroupingKey_Deterministic` | [Unit] |
| `Aging_ComputesInASingleGroupedQuery_NoPerCounterpartyRoundTrip` | [Unit] |
| `Aging_EmptyWindow_ReturnsEmptyRowsAndZeroTotals_NotFound` | [Unit] |
| `CounterpartyBalances_OverdueOutstanding_EqualsTotalMinusCurrentBucket` | [Unit] |
| `CounterpartyBalances_OldestDueDate_IsEarliestInScopeDueDate` | [Unit] |
| `CounterpartyBalances_ZeroOutstandingCounterparty_OmittedFromTotalCount` | [Unit] |
| `CounterpartyBalances_TotalOutstanding_MatchesAgingForSamePair` | [Unit] |
| `CounterpartyBalances_UnknownCounterparty_ReturnsEmpty_NotFound` | [Unit] |
| `CounterpartyBalances_RespectsPageSizeCap_200` | [Unit] |

### 6.4 As-of date & dual-currency reporting (Unit — faked `ICountryStrategy`)

| Test name | Kind |
|---|---|
| `AsOfDate_Today_UsesProjectionSettledAmount` | [Unit] |
| `AsOfDate_Historical_DerivesSettledFromAllocationsUpToDate` | [Unit] |
| `AsOfDate_Historical_ExcludesAllocationsOfCancelledAndReversedPayments` | [Unit] |
| `AsOfDate_Historical_ExcludesAllocationsOfDraftPayments` | [Unit] |
| `AsOfDate_Now_AllocationDerivedSettled_EqualsProjectionSettledAmount` | [Unit] |
| `AsOfDate_ExcludesItemsIssuedAfterAsOfDate_FromCountsAndTotals` | [Unit] |
| `DualCurrency_ReportsTransactionalAndBaseOutstanding` | [Unit] |
| `DualCurrency_BaseOutstanding_RoundsThroughCountryStrategyApplyTaxRounding` | [Unit] |
| `DualCurrency_BaseCurrencyItem_BaseOutstandingEqualsOutstanding_RateIsOne` | [Unit] |
| `DualCurrency_MixedCurrencyCounterparty_ProducesOneRowPerCurrency_NoCrossCurrencyTotal` | [Unit] |
| `DualCurrency_UsesFrozenBookingExchangeRate_NoRateLookup` | [Unit] |

### 6.5 Validation, permissions & no-caching (Unit)

> The §3.1 surface is covered at TWO levels, in two fixtures. `AgingValidationTests` drives the rules through `AgingService`, proving the code a caller actually receives and that it is returned BEFORE any query runs (`Validate_MissingAsOfDate_…`, `Validate_MissingDirection_…`, `Validate_UnknownDirectionValue_…`, `Validate_NonAscendingBuckets_…_BeforeAnyQueryRuns`, plus the two `AgingService_DoesNot…` dependency assertions). `AgingQueryValidatorTests` drives the three FluentValidation validators over the shared `AgingQueryRules` directly (`Validate_ValidQueries_…`, `Validate_OpenItemsWithoutAsOfDate_…`, `Validate_ReportWithoutAsOfDate_…`, `Validate_NonAscendingBuckets_ReturnsInvalidAgingBuckets`). Three names — `Validate_FutureAsOfDate_…`, `Validate_EmptyCounterpartyId_…`, `Validate_MalformedCurrencyCode_…` — deliberately exist in BOTH fixtures and are listed once here. The last three non-`AgingService_` rows live in the shared `PaymentErrorCodesTests`, `PaymentErrorCodeToStatusMapTests`, and `PaymentControllerContractTests` fixtures, tagged `[Category("SDD-PAY-003")]` at the METHOD level, because each reflects over the whole Payments error-code or controller set at once.

| Test name | Kind |
|---|---|
| `Validate_ValidQueries_PassEveryFieldRule` | [Unit] |
| `Validate_MissingAsOfDate_ReturnsInvalidAgingAsOfDate` | [Unit] |
| `Validate_ReportWithoutAsOfDate_ReturnsInvalidAgingAsOfDate` | [Unit] |
| `Validate_OpenItemsWithoutAsOfDate_IsAccepted_DefaultsToToday` | [Unit] |
| `Validate_FutureAsOfDate_ReturnsInvalidAgingAsOfDate` | [Unit] |
| `Validate_MissingDirection_ReturnsInvalidAgingDirection` | [Unit] |
| `Validate_UnknownDirectionValue_ReturnsInvalidAgingDirection` | [Unit] |
| `Validate_EmptyCounterpartyId_ReturnsInvalidCounterpartyId` | [Unit] |
| `Validate_MalformedCurrencyCode_ReturnsInvalidAgingCurrency` | [Unit] |
| `Validate_NonAscendingBuckets_ReturnsInvalidAgingBuckets` | [Unit] |
| `Validate_NonAscendingBuckets_ReturnsInvalidAgingBuckets_BeforeAnyQueryRuns` | [Unit] |
| `PaymentErrorCodes_DefinesAllAgingCodes` | [Unit] |
| `PaymentErrorCodeToStatusMap_AgingCodes_AllResolveTo400` | [Unit] |
| `AgingControllers_DeclareTheReportLevelAgingPermission_ButOpenItemsReadsAsAPayment` | [Unit] |
| `AgingService_DoesNotDependOnCacheService_RecomputesOnEveryCall` | [Unit] |
| `AgingService_DoesNotDependOnWorkflowAuditOrPublishEndpoint` | [Unit] |

### 6.6 Endpoint & wiring (Integration — `[Category("Integration")]`, excluded from the fast offline run)

> **DEFERRED — planned coverage, not evidence (Batch 17).** No `Finance.Payments.API.Tests/Integration/` folder exists yet, so every row in this table is currently UNCOVERED; none of it may be read as shipped. This is the same deliberate sequencing SDD-FIN-006 used — it shipped `Implemented` in Batch 14 with its `[Category("Integration")]` suite deferred, and Batch 15 added it. The highest-value gaps are the three `*_Endpoint_Returns403_WhenPermissionMissing` RBAC checks (nothing else exercises the §2.9 permission split end to end), `Aging_AreNotCached_RecomputeReflectsNewAllocation`, and `Aging_ExcludesInvoice_AfterCancellationEventUpdatesProjection`.
>
> When it lands it MUST run against the shared Testcontainers harness (`src/Tests/Finance.IntegrationTesting` — real SQL Server + Redis + RabbitMQ, minted JWT + real RBAC) in `Finance.Payments.API.Tests/Integration/AgingEndpointIntegrationTests.cs`. The `finance.aging:read` permission is granted via `_factory.PermissionState.Grant(...)`; production still requires the MANUAL auth-service seeding of §2.9 (recorded in `CHG-ENH-003`).

| Test name | Kind |
|---|---|
| `OpenItems_Returns200_WithOutstandingAndBucketLabels_OverRealSql` | [Integration] |
| `Aging_Returns200_WithDefaultBuckets_OverRealSql` | [Integration] |
| `Aging_Returns200_WithCustomBuckets_OverRealSql` | [Integration] |
| `CounterpartyBalances_Returns200_GroupedByCounterpartyAndCurrency_OverRealSql` | [Integration] |
| `Aging_Returns400_WhenAsOfDateInFuture` | [Integration] |
| `Aging_Returns400_WhenBucketsNotAscending` | [Integration] |
| `OpenItems_Endpoint_Returns403_WhenPermissionMissing` | [Integration] |
| `Aging_Endpoint_Returns403_WhenPermissionMissing` | [Integration] |
| `CounterpartyBalances_Endpoint_Returns403_WhenPermissionMissing` | [Integration] |
| `Aging_AreNotCached_RecomputeReflectsNewAllocation` | [Integration] |
| `Aging_ExcludesInvoice_AfterCancellationEventUpdatesProjection` | [Integration] |
| `Endpoints_RoutedThroughGateway_OpenItemsAgingAndCounterpartyBalances` | [Integration] |

## 7. Resolved Decisions & Open Items

### Resolved
- **Lives in the existing Payments service.** Open items, aging, and counterparty balances are read-only aggregations over `finance_payments` — no new service, database, table, migration, event, consumer, audit row, workflow state, or enum. They live in `Finance.Payments.API` (port 6006), per Plan §2 (service #6) and Plan Phase 5. This is deliberately the SAME shape as SDD-FIN-003, which added the GL/trial-balance reads inside the existing `Finance.Journal.API`.
- **Reads the local projection only.** The aggregation source is the SDD-PAY-002 `InvoiceOpenItem` projection (plus `PaymentAllocation` / `Payment` for the historical as-of path). No cross-service call, no cross-database join, no new Refit client (Plan §8). The projection exists precisely so that aging never has to reach into `finance_invoices`.
- **Outstanding formula, eligible statuses and settleable population.** `Outstanding = GrossTotal − SettledAmount`, and an item counts only when `InvoiceStatus ∈ { Confirmed, Posted }` AND its `DocumentType` is settleable by some `SettlementPairing` entry; `Cancelled` and `Reversed` are excluded, `CreditNote` is excluded, and zero-outstanding items are omitted (§2.1).
- **Credit notes are NOT aged, and the exclusion is owned upstream.** `SettlementPairing` (SDD-PAY-002 §2.5 rule 10) admits `CustomerReceipt → { SaleInvoice, DebitNote }` and `SupplierPayment → { PurchaseInvoice }` only, so no payment can settle a credit note and neither of §2.1's exits (`Outstanding == 0.00`, a terminal `InvoiceStatus`) is reachable for one. Left in, a `200.00` credit note to a customer would be reported by `/aging?direction=AP` and `/counterparty-balances?direction=AP` as a payable ageing `1-30 → 90+` forever — the shipped `InvoiceDocumentTypeMap.DirectionFor` maps `CreditNote → AP` (`src/Interfaces/Invoices/Finance.Invoices.API/Services/InvoiceDocumentTypeMap.cs`, lines 45-51) while the note's own posting moves the CUSTOMER control account — while the AR sub-ledger read `0.00`, diverging both control accounts permanently. The fix is applied at the SOURCE (SDD-PAY-002 §2.3 creates no open item for an unsettleable document type, a silent success) and MIRRORED here as a defense-in-depth predicate over the SAME shared table, never a locally re-derived document-type list (§2.1, §2.10). Credit-note settlement is DEFERRED with the refund/offset feature (§1, §5).
- **Invoice reversal IS excluded — and this batch shipped the dependency that makes it possible.** Before Batch 17, `src/Finance.ServiceModel/Events/Invoices/` held only `InvoiceConfirmedEvent`, `InvoicePostedEvent`, and `InvoiceCancelledEvent`, so a reversed invoice stayed `Posted` in `InvoiceOpenItem` forever. That gap is closed: the folder now also holds `InvoiceReversedEvent.cs`, which the SDD-INV-001 amendment publishes from its §2.7 reversal path, and SDD-PAY-002 §2.3's fourth idempotent consumer (`Finance.Payments.API/Consumers/InvoiceReversedEventConsumer.cs`, alongside the confirmed/posted/cancelled three) flips the mirrored `InvoiceStatus` to `Reversed` (which also removes a reversed invoice from SDD-PAY-002 §2.5's allocation-eligibility set, so real cash can no longer be matched to a document with no ledger balance). This spec adds no event and no consumer of its own; it consumes the mirrored status. Both upstream deliverables are hard prerequisites of §2.1 — if either slips, the mirrored status never becomes `Reversed` and a reversed invoice would silently keep being aged as `Posted`.
- **Grouping key is (`CounterpartyId`, `CurrencyCode`).** Summing transactional amounts across currencies is meaningless, so a multi-currency counterparty gets one row per currency and only the base-currency column is cross-summable. Report grand totals are base-currency only.
- **Dual-currency conversion uses the frozen booking rate.** `BaseOutstanding = ApplyTaxRounding(Outstanding × BookingExchangeRate)` — no current-rate lookup, no revaluation (deferred to SDD-FIN-005).
- **Default buckets, configurable per request.** `Current` / `1-30` / `31-60` / `61-90` / `90+` from the boundaries `30,60,90`; a caller MAY pass up to 6 ascending boundaries. Bucket assignment lives in a pure calculator so it is unit-testable without a database.
- **Deterministic paging.** The open-item list appends the projection PK (`InvoiceId`) as the final sort term per SDD-INFRA-005. The two grouped surfaces have no entity PK, so their final deterministic term is the composite grouping key (`CounterpartyId`, `CurrencyCode`) — the same guarantee, expressed on the only key a grouped row has.
- **No caching.** Aging is derived from transactional data → MUST NOT be cached (SDD-INFRA-004); no `ICacheService<T>` in the read path and no `finance-payments` cache prefix registration for this spec.
- **Permissions.** `finance.payment:read` for `/open-items` (the Payments service already owns that data) and the NEW `finance.aging:read` for `/aging` + `/counterparty-balances`, so a finance-reporting/collections role can see the roll-ups without seeing individual payment records. Because SDD-INT-AUTH-001 permission auto-registration is still deferred, `finance.aging:read` MUST be seeded MANUALLY in the auth-service in this batch — otherwise both reports return `403` for everyone. That deployment obligation is recorded in `CHG-ENH-003` (§2.9); the record exists, the seeding itself is an operator step outside this repository and is still outstanding.
- **Endpoints.** `GET /api/v1/open-items` (query: `FilterRequest`, `direction?`, `counterpartyId?`, `currencyCode?`, `overdueOnly?`, `asOfDate?`), `GET /api/v1/aging` (query: `asOfDate`, `direction`, `counterpartyId?`, `currencyCode?`, `buckets?`), `GET /api/v1/counterparty-balances` (query: `asOfDate`, `direction`, `currencyCode?`, `FilterRequest`).
- **As-of settled amount — dual path, chosen by date.** `asOfDate` = today reads the maintained projection column; a past `asOfDate` replays the allocation rows by `AllocatedAt` restricted to `Confirmed`/`Posted` payments. The two MUST agree at "now", and that equality is a test, not an assumption (§2.3).
- **`OpenItemDto` is owned here, and it is the only open-item shape.** It is declared by this spec in `src/Finance.ServiceModel/Payments/OpenItemDto.cs` because it carries the computed report fields (`Outstanding`, `BaseOutstanding`, `DaysPastDue`, `AgingBucket`) that only this aggregation produces. SDD-PAY-002 declares no projection-mirror DTO — its list endpoint returns `PaymentAllocationDto` only — so there is exactly one open-item DTO in the solution and no mapping between two near-identical shapes (§2.5).
- **Projection indexes are owned by SDD-PAY-002 §2.12 — this spec requests none.** The aging/balances queries filter on (`Direction`, `InvoiceStatus`), narrow by `CounterpartyId`/`CurrencyCode`, and order by `DueDate`. The index set that makes this cheap — `IX_InvoiceOpenItems_CounterpartyId`, `IX_InvoiceOpenItems_DueDate`, and the composite `IX_InvoiceOpenItems_Direction_InvoiceStatus_CounterpartyId_DueDate` — is declared once in SDD-PAY-002 §2.12 as part of the projection's EF configuration. This spec adds **no additional index** and MUST NOT restate a different name or column order.
- **The booking rate is real.** `BookingExchangeRate` on the projection traces back to `Invoice.ExchangeRate`, frozen at invoice creation and carried on `InvoiceConfirmedEvent` (SDD-INV-001 §2.14), so `BaseOutstanding` is a genuine base-currency figure for a foreign-currency invoice rather than a `1.000000` placeholder (§2.2).

### Open / deferred (for the Phase-2 implementator)
- **Row cap / paging for the aging report.** `/aging` returns all matching counterparty rows for the requested narrowing (it is a report, not a list). If a deployment has thousands of counterparties, adding `FilterRequest` paging over the counterparty rows is an additive v1.x change — recommend paging by counterparty row, keeping the report-level bucket totals computed over the whole matched set, not just the page. Decide when the first real data volume is known.
- **Bucket label format.** The labels are emitted as `Current`, `1-30`, `31-60`, `61-90`, `90+`. If the frontend prefers pure numeric boundaries with client-side labelling, the response already carries `FromDaysPastDue`/`ToDaysPastDue` per bucket; keep both. Confirm with the SDD-UI-001 frontend phase before changing the label strings, since they are data and not translation keys.
- **Unallocated cash netting.** v1 keeps aging invoice-only, so a counterparty sitting on unallocated cash shows the full invoice outstanding. Netting it in would change the meaning of `Outstanding` (breaking, §5); the recommended future shape is an opt-in `includeUnallocatedCash` flag plus a separate `UnallocatedCash` field rather than a silent netting.
- **Sub-ledger ↔ GL reconciliation.** The AR/AP totals here should equal the `411`/`401` control-account balances from SDD-FIN-003, but v1 asserts nothing — the two live in different services with no read client between them. The reconciliation report is a Phase-7 SDD-RPT-* deliverable; until then a mismatch is discovered only by an accountant. For a foreign-currency document the two figures are not even expected to agree in base currency yet — see the next item.
- **The general ledger is currency-naive today — a PRE-EXISTING SDD-FIN-006 limitation this spec MUST NOT fix.** `PostingEngine.BuildLine` (`src/Interfaces/Journal/Finance.Journal.API/Services/PostingEngine.cs`, lines 153-170) hard-codes `ExchangeRate = 1.000000m` and sets `BaseDebitAmount`/`BaseCreditAmount` to the SAME transactional amount it puts on `DebitAmount`/`CreditAmount`; `CheckBalanced` then compares only those base columns. Every document type is posted that way — invoices included: the shipped `InvoiceConfirmedEventConsumer` hands the engine the TRANSACTIONAL `NetTotal`/`TaxTotal`/`GrossTotal` with `CurrencyCode = message.CurrencyCode`, and SDD-FIN-006 §2.3 already records the rate as `1.000000` "for a base-currency context — deferred FX". So for a foreign-currency document the GL's base-amount columns hold TRANSACTIONAL units, not base units. Consequence for this spec: `BaseOutstanding` here is a REAL booking-rate figure (§2.2) and is therefore the more correct of the two, but it will NOT tie to the GL control-account base balance for a foreign-currency document until the ledger becomes multi-currency-correct. That work belongs to the not-yet-authored SDD-FIN-005 (Multi-Currency Engine) and is out of scope for this batch: this spec MUST NOT compensate for the GL, MUST NOT re-derive base amounts from it, and MUST NOT be read as implying the GL is multi-currency-correct.
- **Performance / materialization.** v1 aggregates live on read, mirroring the SDD-FIN-003 decision. If aging latency over a large open-item set becomes a problem, an allocation-event-fed outstanding snapshot (or an indexed view) is a future `CHG-ENH-*` that MUST reproduce identical results to this live aggregation.
