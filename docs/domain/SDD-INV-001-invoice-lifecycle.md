# SDD-INV-001 — Invoice Lifecycle (Purchase + Sale + Credit/Debit Notes)

> Status: Implemented (Batch 16 — core shipped + tested; 68 unit tests green per the CLAUDE.md §0 lifecycle (`Implemented` = code shipped + tests pass + in force, may carry explicit `Deferred:` notes). The `Invoice` aggregate (Purchase Invoice, Sale Invoice, Credit Note, Debit Note as one aggregate discriminated by `DocumentType`), its `Draft → Confirmed → Posted` lifecycle (+ `Cancelled`/`Reversed`) via `IWorkflowEngine<Invoice>`, country-aware tax calculation/rounding through `ICountryStrategy`, gapless document numbering through `ISequenceGenerator`, and the event-driven Confirm→Post posting handshake with the Journal service via the transactional outbox are all shipped. Deferred: the automatic `Posted → Reversed` full-offset transition on a Credit Note (§5/§7 — depends on the deferred CREDIT_NOTE/DEBIT_NOTE posting-rule templates), counterparty enrichment (SDD-INT-WH-002), FX rate lookup (SDD-FIN-005), and the frontend phase. Document-triggered draft creation from Warehouse events is the sibling spec SDD-INT-WH-001.)
> Owner: Finance
> Last updated: 2026-06-10
> Category: Domain
> Service: `Finance.Invoices.API` — port **6005**, database `finance_invoices` (FINANCE-MICROSERVICES-PLAN §2, service #5 = "Purchase + Sale invoices + lifecycle"; §3 — `Finance.Invoices.DBModel` owns schema `finance_invoices` incl. outbox)
> Related: SDD-FIN-002 (Journal Entry Lifecycle — the lifecycle this spec mirrors; the invoice's posting DELEGATES the journal entry to the Journal service and does NOT redefine the JE; the Journal service publishes the dedicated back-event `InvoicePostedEvent` once it posts), SDD-FIN-006 (Posting Engine + Posting Rules — the Journal-side consumer turns `InvoiceConfirmedEvent` into a balanced JE via `IPostingEngine.ApplyAsync`; this spec does NOT reimplement posting rules), SDD-CTRY-001 (Country Strategy — tax calc/rounding, document numbering, posting-rule keys all go through `ICountryStrategy`; this spec GROWS the interface with the tax + document-number members), SDD-INFRA-003 (Sequence Generation — gapless `PINV`/`SINV`/`CN`/`DN` numbers at confirm), SDD-INFRA-006 (transactional outbox + idempotency — `InvoiceConfirmedEvent` published atomically; the JE-posted-back consumer is idempotent), SDD-INFRA-008 (Workflow Engine — `IWorkflowEngine<Invoice>`, `AllowedNextStates`, guards, status history, RowVersion), SDD-INFRA-009 (base service/controller, `Result<T>`, `SearchableServiceBase`, `BaseApiController`), SDD-INFRA-005 (list filtering/paging), SDD-INFRA-004 (caching — invoices are transactional → MUST NOT be cached), SDD-INFRA-007 (cross-aggregate guards — period-open, totals reconciliation), SDD-AUDIT-001 (audit-first; confirmed/posted invoices immutable; corrections via Credit/Debit Note), SDD-FIN-005 (Decimal arithmetic — `decimal`/`DECIMAL(18,2)` amounts, `DECIMAL(18,6)` rates; tax rounding via `ICountryStrategy`), SDD-FIN-004 (Fiscal Period Management — the period-open guard at confirm/post reuses the same period-status seam as SDD-FIN-002), SDD-INT-AUTH-001 (RBAC — `finance.invoice:read|create|confirm|post|cancel`), SDD-INT-WH-001 (Warehouse inbound events — system-created drafts; uses the SAME domain create path as manual create), SDD-OBS-001 (tracing, structured logging), SDD-EVTLOG-001 (event archive consumes `InvoiceConfirmedEvent`)
> ISA-95: Level 4 (Business Planning & Logistics) — financial Documents

---

## 1. Context & Scope

This spec defines the **`Invoice` aggregate** and its **lifecycle**: the state machine that moves an invoice through `Draft → Confirmed → Posted` (plus `Cancelled` and the `Reversed`/credit-note correction path), the side effects each transition triggers, the country-aware tax computation that produces its totals, and the handshake by which a confirmed invoice becomes a posted journal entry.

A single aggregate `Invoice` represents all four financial documents — **Purchase Invoice**, **Sale Invoice**, **Credit Note**, **Debit Note** — discriminated by a `DocumentType` enum and a `Direction` (`AP` payable / `AR` receivable). The header carries totals; the `invoice_lines` child collection carries the per-line breakdown. This is the documents tier of the platform (FINANCE-MICROSERVICES-PLAN §2, service #5).

Three principles govern the lifecycle, mirroring SDD-FIN-002:
1. **Confirmed/Posted is immutable (SDD-AUDIT-001).** Once an invoice is `Confirmed` it has a legally-issued gapless document number and MUST NEVER have its header or lines edited. A correction is made by issuing a **Credit Note** (to reduce/cancel) or a **Debit Note** (to increase) — never by UPDATEing the confirmed/posted row. A confirmed invoice MAY be `Cancelled` (voided) only before posting; a posted invoice is corrected only by a note (`Reversed` path, §2.7).
2. **Numbering is gapless and country-formatted.** The gapless document number (`ISequenceGenerator`, SDD-INFRA-003) is assigned at **Confirm** — its format comes from `ICountryStrategy.GenerateDocumentNumber` (SDD-CTRY-001) per document type (`PINV`/`SINV`/`CN`/`DN`).
3. **Posting is delegated, never reimplemented.** The Invoice service does NOT build double-entry lines, number journal entries, or write GL. On confirm it publishes `InvoiceConfirmedEvent` through the transactional outbox; the **Journal** service consumes it and posts the journal entry via the Posting Engine (SDD-FIN-006) using posting rules (SDD-FIN-002/SDD-FIN-006 own the JE entirely). The invoice moves to `Posted` and stores the resulting `JournalEntryId` when the Journal service publishes back the dedicated `InvoicePostedEvent` (§2.5). A dedicated back-event is used rather than the generic `JournalEntryPostedEvent` (which is published for every journal entry and is already consumed by the EventLog archive); a dedicated event keeps the invoice→posting correlation explicit and unambiguous and avoids fragile routing/matching of a multi-purpose event.

**Tax computation goes through the country strategy.** Line and document totals (net, tax, gross) MUST be computed via `ICountryStrategy` tax methods and rounded via `ICountryStrategy.ApplyTaxRounding`, using `decimal` only (SDD-FIN-005). The core invoice code MUST remain country-agnostic — it MUST NOT hard-code a VAT rate, a rounding rule, or a number format. This spec GROWS the `ICountryStrategy` interface (currently three members per SDD-CTRY-001) with the tax + document-number members (§5).

**ISA-95 classification.** An `Invoice` is an ISA-95 **Level 4 (Business Planning & Logistics)** financial **Document** (ISA-95 / IEC 62264 Part 1, §5). The **confirm**, **post**, **cancel**, and **note/reverse** operations are Level-4 business transactions that change the document's recorded state; each MUST emit an **immutable domain event** for state changes (`InvoiceConfirmedEvent`, `InvoiceCancelledEvent`; the posted state is reflected by the dedicated `InvoicePostedEvent` published by the Journal service) and an immutable audit row (SDD-AUDIT-001). Each `InvoiceLine` is the Level-4 transaction-line / component of the document (parity with `JournalEntryLine` under SDD-FIN-001/-002), carrying the per-line net/tax/gross breakdown that rolls up to the header totals. The `invoice_status_history` rows are append-only Level-4 audit sub-records. No Level-3 (MES) activity is modelled. The counterparty (customer/supplier) is Warehouse-owned Level-4 master data referenced by GUID, NOT a foreign key (cross-service joins are forbidden, Plan §8).

**Scope — covered (v1):**
- The `Invoice` aggregate (header `invoices`, child `invoice_lines`) with `DocumentType` (`PurchaseInvoice` | `SaleInvoice` | `CreditNote` | `DebitNote`), `Direction` (`AP` | `AR`).
- The `Draft → Confirmed → Posted` state machine via `IWorkflowEngine<Invoice>`, plus `Cancelled` (from `Draft`/`Confirmed`) and the Credit/Debit-Note correction path for a `Posted` invoice (§2.7).
- CRUD on drafts: create (manual user-entered AND system-created from Warehouse events — SDD-INT-WH-001), update draft, get, list (filtered/paged), cancel.
- Country-aware tax computation + rounding via `ICountryStrategy`; totals reconciliation (lines sum to header; net + tax = gross to the cent).
- Confirm: gapless country-formatted document number, totals freeze, audit-first → outbox `InvoiceConfirmedEvent`, status-history append — all atomic.
- The Confirm→Post handshake with the Journal service (§2.5) and the inbound `InvoicePostedEvent` consumer that links `JournalEntryId` and moves `Confirmed → Posted`.
- Immutability enforcement on `Confirmed`/`Posted` invoices; corrections via Credit/Debit Note.
- RBAC, correlation, audit, outbox, no-caching (transactional data).

**Scope — excluded (DEFERRED):**
- **The journal entry itself** — drafting/numbering/balancing/GL of the JE is SDD-FIN-002 / SDD-FIN-006 / SDD-FIN-003. This spec only triggers it.
- **The Warehouse event consumers** that create system drafts — SDD-INT-WH-001 (sibling spec, same batch). This spec defines the create path they call; it does NOT define the event contracts or the consumers.
- **Payment / settlement / allocation** against the invoice (outstanding balance, AP/AR aging) — SDD-PAY-001 / SDD-PAY-002.
- **VAT journals / regulatory НАП export** of invoices — SDD-RPT-003 / SDD-INT-NAP-001.
- **FX rate resolution** — a non-base-currency invoice supplies its rate; automatic rate lookup is SDD-FIN-005. v1 SHOULD assume a base-currency context.
- **Multi-line tax codes / mixed VAT rates per line beyond a single `TaxRate` per line** — the line carries one `TaxRate`; richer tax-code matrices are SDD-CTRY-BG-001.
- **Approval / maker-checker** between `Draft` and `Confirmed` — future `CHG-FEAT-*`.

## 2. Behavior

> **Service/controller contract (SDD-INFRA-009).** `InvoiceService` MUST inherit `SearchableServiceBase<Invoice, InvoiceDto, InvoicesDbContext>` (and `BaseEntityService<InvoicesDbContext>`). Every public method MUST return `Result` / `Result<T>` — never `null`, never a thrown exception for a business outcome. `InvoicesController` inherits `BaseApiController` and translates every result via `ToActionResult(...)`. State transitions MUST go through `IWorkflowEngine<Invoice>` (SDD-INFRA-008); the service owns `SaveChanges` / `RowVersion` / status-history inside the outbox transaction. `CancellationToken` MUST be threaded controller → service → DB / sequence / publish.

### 2.1 State machine (MUST — SDD-INFRA-008)
- `Invoice` MUST be a workflow aggregate with these states and `AllowedNextStates`:
  - `Draft` → { `Confirmed`, `Cancelled` } (and a `Draft` MAY be **deleted/updated** — a removal/edit, not a transition).
  - `Confirmed` → { `Posted`, `Cancelled` }.
  - `Posted` → { `Reversed` } (a posted invoice is corrected only by the Credit/Debit-Note path, §2.7; the `Reversed` flag records that a correcting note offsets it).
  - `Cancelled` → { } (terminal).
  - `Reversed` → { } (terminal).
- Any transition not in `AllowedNextStates` MUST be rejected by the engine and surfaced as `INVALID_INVOICE_STATE_TRANSITION` (the domain alias for the engine's generic `INVALID_STATE_TRANSITION`, SDD-INFRA-008 §4, mirroring SDD-FIN-002 §2.1).
- A new invoice MUST be created in `Draft` (§2.3).

### 2.2 Workflow guards & ordering (MUST)
- The `Draft → Confirmed` transition MUST run, in order:
  1. The full validation surface (§3): at least one line, every line valid, totals reconciled (lines sum to header; net + tax = gross to the cent), counterparty present, currency valid, dates valid.
  2. The **period-open guard** for the invoice `IssueDate` — a closed/locked period MUST short-circuit with `INVOICE_PERIOD_CLOSED`. This reuses the same period-status seam SDD-FIN-002 §2.7 uses (`IPostingPeriodGuard`-equivalent backed by SDD-FIN-004); until SDD-FIN-004's lookup is reachable from this service the default is always-open.
- A guard failure MUST short-circuit with no side effects (no number burned, no event, no audit row) and surface as `Result.Failure(...)` carrying the failing guard's code.

### 2.3 Create draft (MUST)
- `POST /api/v1/invoices` MUST create an `Invoice` in `Draft` with the caller-supplied `DocumentType`, counterparty reference, currency, dates, and lines. Requires `finance.invoice:create`.
- The SAME domain create path MUST serve both **manual** (user-entered via the endpoint) and **system-created** drafts (from Warehouse events — SDD-INT-WH-001). SDD-INT-WH-001's consumers MUST call this service method, NOT a parallel creation path.
- `Direction` MUST be derived from `DocumentType` and frozen: `SaleInvoice`/`DebitNote` → `AR`; `PurchaseInvoice`/`CreditNote` → `AP`. (A Credit Note reduces what a customer owes when paired with a sale, but in v1 `Direction` follows the table above; the pairing/offset is the note's `CorrectsInvoiceId` link, §2.7.)
- `DocumentNumber` MUST remain NULL while `Draft` — the gapless number is assigned only at Confirm (§2.4).
- Totals (`NetTotal`, `TaxTotal`, `GrossTotal`) MUST be computed on create from the lines via `ICountryStrategy` (§2.8) and stored; they MUST reconcile (§3.2) before the draft is saved — but a draft MAY be saved even if it would not yet confirm (e.g. zero lines) ONLY when created by a system event that will be completed later; a **manual** create MUST require ≥ 1 line (`INVOICE_LINES_REQUIRED`).
- `BaseCurrencyCode` MUST be set from `ICountryStrategy.BaseCurrencyCode` and frozen on the invoice.
- `CorrelationId` MUST be captured from `ICorrelationIdAccessor`; `CreatedAt` server-side (`SYSDATETIMEOFFSET()`); `CreatedBy` from the authenticated principal (or the system identity for event-created drafts).
- Draft creation MUST write an audit `Create` row (`BeforeJson = null`). No domain event is published on draft creation.

### 2.4 Confirm (MUST)
- `POST /api/v1/invoices/{id}/confirm` MUST transition a `Draft` invoice to `Confirmed` via `IWorkflowEngine<Invoice>`. Requires `finance.invoice:confirm`.
- The invoice MUST be in `Draft`; otherwise `INVOICE_NOT_DRAFT`.
- On a successful transition the service MUST, within a single SaveChanges/outbox transaction and in this order:
  1. Run the §2.2 guards (full validation + period-open).
  2. Recompute and freeze totals via `ICountryStrategy` (§2.8); re-assert reconciliation (`INVOICE_TOTALS_MISMATCH` if it fails).
  3. Assign `DocumentNumber` from `ISequenceGenerator.NextAsync(<key>, ct)` where `<key>` is the per-document-type sequence key (`PINV`/`SINV`/`CN`/`DN`, SDD-INFRA-003 §2.1) and the FORMAT comes from `ICountryStrategy.GenerateDocumentNumber` (SDD-CTRY-001). The number MUST be allocated inside the same transaction (no burn on rollback — SDD-INFRA-003 §2.4).
  4. Stamp `ConfirmedAt = SYSDATETIMEOFFSET()` and `ConfirmedBy`; set `Status = Confirmed`.
  5. Write an audit `StateChange` row (`EventType = "InvoiceConfirmed"`, `BeforeJson` = draft snapshot, `AfterJson` = confirmed snapshot) **before** the outbox row (audit-first, SDD-AUDIT-001).
  6. Enqueue `InvoiceConfirmedEvent` to the outbox (atomic with the DB write — no `await _bus.Publish` outside the outbox, no try/catch, SDD-INFRA-006).
  7. Append the `invoice_status_history` row (`Draft → Confirmed`) and increment `RowVersion`.
- Confirm is NOT on SDD-AUDIT-001's mandatory-`Reason` list (it is a routine issuance), so no `Reason` is required.

### 2.5 Posting handshake — Confirm → Posted (MUST)
- **Decision (chosen, simpler option).** Confirm publishes `InvoiceConfirmedEvent`; the **Journal** service is the authority that posts the journal entry (via the Posting Engine, SDD-FIN-006) and publishes back a **dedicated `InvoicePostedEvent`** carrying the source invoice id, the resulting journal entry id, and the journal entry number; the **Invoice** service consumes that event, stores the resulting `JournalEntryId`, and moves the invoice `Confirmed → Posted`. The Invoice service NEVER posts a journal entry itself. A dedicated back-event is used rather than the generic `JournalEntryPostedEvent` because that event is published for every posted journal entry (not only invoice-originated ones) and is already consumed by the EventLog archive; routing and correlation-matching a multi-purpose event into the Invoice service is fragile, whereas a dedicated event makes the invoice→posting link explicit.
- `InvoiceConfirmedEvent` (§2.10) MUST carry enough for the Journal-side posting rule to materialize a balanced entry: invoice id, document type, direction, counterparty id, currency, base currency, issue date, the chosen `PostingRuleKey` (derived from `DocumentType` — e.g. `SALE_INVOICE` / `PURCHASE_INVOICE`), and the net/tax/gross amounts. The Journal consumer calls `IPostingEngine.ApplyAsync` with those amounts (SDD-FIN-006 §2.3) — this spec does NOT define the rule materialization.
- `POST /api/v1/invoices/{id}/post` (explicit) MUST be the operator-driven equivalent of awaiting the handshake: it MUST verify the invoice is `Confirmed`, and either (a) if the linked `JournalEntryId` is already recorded (the back-event arrived), confirm the `Posted` transition; or (b) if not yet linked, return `INVOICE_NOT_CONFIRMED`/`INVOICE_POSTING_PENDING` so the caller retries. The asynchronous back-event consumer (next bullet) is the primary path; this endpoint is for visibility/manual completion. Requires `finance.invoice:post`.
- The inbound `InvoicePostedEvent` consumer (in `Finance.Invoices.API/Consumers/`) MUST be wrapped in `IdempotencyFilter<T>` (SDD-INFRA-006), MUST match the event to the source invoice by the `InvoiceId` carried on the event, MUST set `JournalEntryId`, stamp `PostedAt`, transition `Confirmed → Posted` via the workflow engine, write an audit `StateChange` (`EventType = "InvoicePosted"`), append status history, and increment `RowVersion` — all in one transaction. A replay MUST be a no-op (the invoice is already `Posted`).
- **Deferred alternative (documented, not chosen):** *optimistic posting* — Confirm could itself post the JE synchronously (call `IPostingEngine.ApplyAsync` across the service boundary) and store `JournalEntryId` immediately, moving straight to `Posted`. Rejected for v1 because it couples the Invoice transaction to the Journal service's availability and breaks the database-per-service / async-mesh boundary (Plan §8). The event-driven handshake keeps the services decoupled and the post resilient (outbox + idempotency). The optimistic path MAY be revisited in a future `CHG-ENH-*` if synchronous posting is required for UX.

### 2.6 Update / cancel draft & confirmed (MUST)
- `PUT /api/v1/invoices/{id}` MUST update a `Draft` invoice only (counterparty, dates, lines). Requires `finance.invoice:create`.
  - An update against a `Confirmed`/`Posted`/`Cancelled`/`Reversed` invoice MUST be rejected with `INVOICE_POSTED_IMMUTABLE` — confirmed and later states are immutable (§2.9).
  - The updated draft MUST recompute totals via `ICountryStrategy` and re-reconcile (§3.2) before persisting.
  - Optimistic concurrency MUST be enforced via the base64 `RowVersion`; a stale token MUST yield `CONCURRENT_MODIFICATION`.
  - An update MUST write an audit `Update` row.
- `POST /api/v1/invoices/{id}/cancel` MUST cancel (void) a `Draft` or `Confirmed` invoice (transition to `Cancelled`). Requires `finance.invoice:cancel`.
  - Cancelling a `Posted`/`Cancelled`/`Reversed` invoice MUST be rejected with `INVALID_INVOICE_STATE_TRANSITION` (a posted invoice is corrected by a note, never cancelled).
  - A non-empty `Reason` MUST be supplied (cancellation voids a numbered document — sensitive); a missing reason MUST yield `INVOICE_CANCEL_REASON_REQUIRED`.
  - Cancelling a `Confirmed` invoice MUST NOT reuse or release its gapless document number (numbers are gapless and never recycled — НАП); the cancelled row keeps its number with `Status = Cancelled`.
  - Cancel MUST write an audit `StateChange` row (with the `Reason`) and publish `InvoiceCancelledEvent` via the outbox.

### 2.7 Correction of a posted invoice via Credit/Debit Note (MUST — the immutability-preserving correction)
- A `Posted` invoice MUST NEVER be edited or cancelled. To correct it, the operator issues a **Credit Note** (reduces the original) or **Debit Note** (increases it) as a NEW `Invoice` whose `CorrectsInvoiceId` links back to the original.
- The note is a normal invoice document: created as `Draft`, confirmed (its own gapless `CN`/`DN` number + `InvoiceConfirmedEvent` → its own posting), and posted via the same handshake (§2.5). Its posting rule (`CREDIT_NOTE`/`DEBIT_NOTE`) produces the offsetting/augmenting journal entry.
- When a Credit Note that **fully** offsets the original is posted, the original MAY transition `Posted → Reversed` to record that it is fully corrected; a partial note leaves the original `Posted`. The decision (full-offset → `Reversed`) MUST be explicit and recorded in an audit `StateChange` (`EventType = "InvoiceReversed"`) with the linking note id and `Reason`. The original's lines/number MUST NOT be mutated (only the state flag + `RowVersion` + status history).
- This mirrors SDD-FIN-002 §2.6 reversal: nothing is overwritten; both the original (with its `Reversed` flag) and the note (a separate posted document) persist.

### 2.8 Tax computation & totals (MUST — SDD-CTRY-001 / SDD-FIN-005)
- Each `invoice_line` MUST carry `Quantity`, `UnitPrice`, `TaxRate`, and the derived `LineNet`, `LineTax`, `LineGross`. `LineNet = round(Quantity × UnitPrice)`, `LineTax = ICountryStrategy.ApplyTaxRounding(LineNet × TaxRate)`, `LineGross = LineNet + LineTax`. All arithmetic MUST be `decimal` (`DECIMAL(18,2)` amounts, `DECIMAL(18,6)` rates) — never `double`/`float`.
- Rounding MUST go through `ICountryStrategy.ApplyTaxRounding` — the core MUST NOT inline a rounding mode. Tax-rate validity/lookup MUST go through `ICountryStrategy` (the country owns which rates are legal).
- Document totals MUST be the sum of the line components: `NetTotal = Σ LineNet`, `TaxTotal = Σ LineTax`, `GrossTotal = Σ LineGross`, and `GrossTotal = NetTotal + TaxTotal` MUST hold to the cent. A mismatch MUST yield `INVOICE_TOTALS_MISMATCH`.
- Totals MUST be (re)computed by the service from the lines — a client-supplied total MUST be ignored or validated against the computed value (never trusted blindly).

> **`INVOICE_TOTALS_MISMATCH` is a defensive (fail-fast) invariant, unreachable through the v1 service paths.** Because the service ALWAYS recomputes the header totals from the lines via `InvoiceTotalsCalculator` (`NetTotal = Σ LineNet`, `TaxTotal = Σ LineTax`, `GrossTotal = Σ LineGross`) immediately before the reconciliation check — satisfying the "client totals MUST be ignored / never trusted" rule above — the reconcile guard cannot fail in practice on a v1 create/update/confirm path. It is retained as **defense-in-depth**: it catches a future regression should the recompute ever be bypassed (e.g. a new code path that trusts a client total or mutates totals out of band). It is documented in the error table (§4) as a defensive code, not a routinely-reachable client outcome. The shipped tests assert this honestly — they verify the recompute produces reconciling totals and that the guard is present, not that a normal client request can trip it.

### 2.9 Immutability of confirmed/posted invoices (MUST — SDD-AUDIT-001)
- A `Confirmed`, `Posted`, `Cancelled`, or `Reversed` invoice's header, lines, totals, and `DocumentNumber` MUST NEVER be UPDATEd. The only permitted mutations are the state-flag transitions defined above (confirm→post link, post→reversed flag) with their `RowVersion`/status-history.
- `PUT` against a non-`Draft` invoice MUST be rejected with `INVOICE_POSTED_IMMUTABLE`.
- There is NO hard `DELETE` of a confirmed-or-later invoice; a `Draft` MAY be deleted (`DELETE /api/v1/invoices/{id}`, requires `finance.invoice:create`), writing an audit delete row. A draft has no `DocumentNumber`, so no gapless number is consumed.

### 2.10 List & get (MUST)
- `GET /api/v1/invoices` MUST accept a `FilterRequest` and return `PagedResult<InvoiceDto>` (SDD-INFRA-005), default-ordered by `IssueDate` descending then `Id` (PK appended as the final deterministic term). `PageSize` capped at 200. Requires `finance.invoice:read`.
  - Filterable/sortable surface MUST be opt-in via `[Filterable]`/`[Sortable]` on `Invoice`: `DocumentNumber`, `DocumentType`, `Direction`, `Status`, `CounterpartyId`, `CurrencyCode`, `IssueDate`, `DueDate`.
  - The list MUST NOT be cached (invoices are transactional data — SDD-INFRA-004 forbids caching them).
- `GET /api/v1/invoices/{id}` MUST return the invoice with its lines, or `INVOICE_NOT_FOUND` (404). Requires `finance.invoice:read`. MUST NOT be cached.

### 2.11 Domain events (MUST — SDD-INFRA-006)
- Events MUST be `sealed record` types implementing `IFinanceEvent` in `Finance.ServiceModel/Events/Invoices/`, with `required` properties + `MessageId` + `CorrelationId` + `OccurredAt`, published via the transactional outbox only.
- `InvoiceConfirmedEvent` MUST carry: `MessageId`, `CorrelationId`, `OccurredAt`, `InvoiceId`, `DocumentNumber`, `DocumentType`, `Direction`, `CounterpartyId`, `CurrencyCode`, `BaseCurrencyCode`, `IssueDate`, `PostingRuleKey`, `NetTotal`, `TaxTotal`, `GrossTotal`.
- `InvoiceCancelledEvent` MUST carry: `MessageId`, `CorrelationId`, `OccurredAt`, `InvoiceId`, `DocumentNumber`, `Reason`.
- `InvoicePostedEvent` (the dedicated back-event published by the **Journal** service through ITS outbox once it posts the JE, §2.5) MUST carry: `MessageId`, `CorrelationId`, `OccurredAt`, `InvoiceId`, `JournalEntryId`, `JournalEntryNumber`. It lives in `Finance.ServiceModel/Events/Invoices/` alongside the invoice-published events because it is part of the invoice posting contract, even though the Journal service is its publisher.
- `InvoiceConfirmedEvent` and `InvoiceCancelledEvent` MUST be published by the Invoice service via the EF Core transactional outbox configured on `InvoicesDbContext`, atomic with the DB transaction; `InvoicePostedEvent` is published by the Journal service via its own outbox. The publishers MUST NOT publish outside the outbox and MUST NOT wrap the publish in try/catch.

### 2.12 Cross-cutting obligations (MUST)
- Every endpoint MUST be protected by `[RequirePermission("finance.invoice:<action>")]` decoded via the shared `Warehouse.Auth.Shared` package (SDD-INT-AUTH-001).
- `CorrelationId` MUST flow via `ICorrelationIdAccessor`/`CorrelationIdMiddleware` and be stamped onto every published event (SDD-INFRA-001/006). The Journal-side post MUST run under the same correlation so the posted JE event carries the originating invoice correlation id.
- The service MUST be traced via OpenTelemetry with the `correlation_id` Activity tag (SDD-OBS-001); logging MUST use NLog structured templates (no string interpolation).

### 2.13 Edge cases (MUST)
- **Confirm with no lines.** `POST .../confirm` on a draft with zero lines MUST return `INVOICE_LINES_REQUIRED` before any number is allocated.
- **Confirm with mismatched totals.** A draft where `Σ LineGross ≠ Σ LineNet + Σ LineTax`, or header totals diverge from the line sums, MUST return `INVOICE_TOTALS_MISMATCH` before any number is allocated. (Per the §2.8 note, this is a defensive fail-fast invariant: the service recomputes header totals from the lines before the check, so it is unreachable through the normal v1 paths and is retained as defense-in-depth.)
- **Re-confirming a confirmed invoice.** `POST .../confirm` on a `Confirmed`/`Posted` invoice MUST return `INVOICE_NOT_DRAFT` — never a second gapless number, never a duplicate `InvoiceConfirmedEvent`.
- **Confirm into a closed period (post-FIN-004).** MUST return `INVOICE_PERIOD_CLOSED`; with the default always-open guard this path is unreachable but the code and test stub MUST exist.
- **Editing/deleting a confirmed-or-later invoice.** `PUT`/`DELETE` on a `Confirmed`/`Posted`/`Cancelled`/`Reversed` invoice MUST return `INVOICE_POSTED_IMMUTABLE`.
- **Cancelling a posted invoice.** MUST return `INVALID_INVOICE_STATE_TRANSITION` (correct via a Credit/Debit Note instead).
- **Duplicate posting back-event (replay).** A redundant `InvoicePostedEvent` for an already-`Posted` invoice MUST be a no-op (idempotency, SDD-INFRA-006) — never a duplicate `JournalEntryId`, never a second `Posted` transition.
- **Concurrent confirm of the same draft.** Two simultaneous confirms — one MUST win; the other MUST fail with `CONCURRENT_MODIFICATION` (RowVersion mismatch).
- **System-created draft from a duplicate Warehouse event.** Handled by SDD-INT-WH-001 (idempotency + source-document dedupe); this spec's create path MUST be safe to call once per source document.

## 3. Validation Rules

### 3.1 Field-level (FluentValidation — request shape)

| Request | Field | Rule | Error code |
|---|---|---|---|
| Create/Update | `DocumentType` | Required, one of the four enum values | `INVALID_INVOICE_DOCUMENT_TYPE` |
| Create/Update | `CounterpartyId` | Required (non-empty GUID) | `INVOICE_COUNTERPARTY_REQUIRED` |
| Create/Update | `CurrencyCode` | Required, ISO 4217 (3 chars) | `INVALID_INVOICE_CURRENCY` |
| Create/Update | `IssueDate` | Required | `INVALID_INVOICE_DATE` |
| Create/Update | `DueDate` | Required, ≥ `IssueDate` | `INVALID_INVOICE_DUE_DATE` |
| Create/Update | `Lines` | Manual create: NotEmpty (≥ 1) | `INVOICE_LINES_REQUIRED` |
| Create/Update | `Lines[].Quantity` | > 0 | `INVALID_INVOICE_LINE` |
| Create/Update | `Lines[].UnitPrice` | ≥ 0 | `INVALID_INVOICE_LINE` |
| Create/Update | `Lines[].TaxRate` | ≥ 0, a rate `ICountryStrategy` recognizes | `INVALID_INVOICE_TAX_RATE` |
| Cancel | `Reason` | NotEmpty | `INVOICE_CANCEL_REASON_REQUIRED` |

### 3.2 Cross-field / cross-aggregate guards (SDD-INFRA-007 / SDD-INFRA-008)

| Transition / op | Guard | Error code |
|---|---|---|
| Create/Update/Confirm | `Σ LineNet + Σ LineTax = Σ LineGross` and header totals equal the line sums, to the cent (defensive — see the §2.8 note: the service recomputes header totals from the lines first, so this guard is unreachable through the v1 paths and is kept as defense-in-depth) | `INVOICE_TOTALS_MISMATCH` |
| Draft → Confirmed | period-open guard for `IssueDate` (SDD-FIN-004 seam; default always-open) | `INVOICE_PERIOD_CLOSED` |
| any illegal transition | `IWorkflowEngine<Invoice>` `AllowedNextStates` | `INVALID_INVOICE_STATE_TRANSITION` |

### 3.3 State-based

| Condition | Rule | Error code |
|---|---|---|
| Confirm a non-`Draft` invoice | Reject | `INVOICE_NOT_DRAFT` |
| Post a non-`Confirmed` invoice / posting not yet linked | Reject | `INVOICE_NOT_CONFIRMED` |
| Update/delete a non-`Draft` invoice | Reject (immutable) | `INVOICE_POSTED_IMMUTABLE` |
| Cancel a `Posted`/`Cancelled`/`Reversed` invoice | Reject | `INVALID_INVOICE_STATE_TRANSITION` |
| Stale `RowVersion` on update/confirm/cancel | Reject | `CONCURRENT_MODIFICATION` |
| Invoice not found (any op) | Reject | `INVOICE_NOT_FOUND` |
| Confirm a draft that already has a `DocumentNumber` (replay) | Reject | `INVOICE_DUPLICATE_DOCUMENT_NUMBER` |

## 4. Error Rules

All errors are RFC-7807 ProblemDetails per SDD-INFRA-001 (`title` = code, `detail` = developer English, `type` = `https://finance.local/errors/{code}`). `BaseApiController.ToActionResult` maps codes to HTTP via `IErrorCodeToStatusMap` (SDD-INFRA-009); services return `Result.Failure(code, detail)`. Constants live in `Finance.Common/ErrorCodes/InvoiceErrorCodes.cs` (SCREAMING_SNAKE_CASE); `CONCURRENT_MODIFICATION` is referenced from `CommonErrorCodes` (single source) — NOT redefined.

| Code | HTTP | Trigger | Type |
|---|---|---|---|
| `INVOICE_NOT_FOUND` | 404 | Invoice id does not exist | Not found |
| `INVOICE_LINES_REQUIRED` | 400 | Manual create/confirm with zero lines | Validation |
| `INVALID_INVOICE_DOCUMENT_TYPE` | 400 | Missing/unknown `DocumentType` | Validation |
| `INVOICE_COUNTERPARTY_REQUIRED` | 400 | Missing counterparty | Validation |
| `INVALID_INVOICE_CURRENCY` | 400 | Missing/invalid currency code | Validation |
| `INVALID_INVOICE_DATE` | 400 | Missing/invalid issue date | Validation |
| `INVALID_INVOICE_DUE_DATE` | 400 | Due date missing or before issue date | Validation |
| `INVALID_INVOICE_LINE` | 400 | Line quantity ≤ 0 or unit price < 0 | Validation |
| `INVALID_INVOICE_TAX_RATE` | 400 | Tax rate negative or not recognized by `ICountryStrategy` | Validation |
| `INVOICE_TOTALS_MISMATCH` | 400 | Lines do not sum to header, or net + tax ≠ gross. **Defensive code** (see the §2.8 note): the service always recomputes header totals from the lines first, so this is unreachable through the v1 service paths — retained as defense-in-depth, not a routinely-reachable client outcome | Validation (cross-field, defensive) |
| `INVOICE_NOT_DRAFT` | 409 | Confirm attempted on a non-`Draft` invoice | Conflict (state) |
| `INVOICE_NOT_CONFIRMED` | 409 | Post attempted on a non-`Confirmed` invoice, or posting not yet linked | Conflict (state) |
| `INVOICE_POSTED_IMMUTABLE` | 409 | Update/delete attempted on a `Confirmed`/`Posted`/`Cancelled`/`Reversed` invoice | Conflict (immutability) |
| `INVALID_INVOICE_STATE_TRANSITION` | 409 | Transition not in `AllowedNextStates` (e.g. cancel a posted invoice) | Conflict (workflow) |
| `INVOICE_PERIOD_CLOSED` | 409 | `IssueDate` falls in a closed/locked period (real check deferred to SDD-FIN-004) | Conflict (period) |
| `INVOICE_DUPLICATE_DOCUMENT_NUMBER` | 409 | A confirm/replay would assign a second document number | Conflict (numbering) |
| `INVOICE_CANCEL_REASON_REQUIRED` | 400 | Cancel called without a non-empty `Reason` | Validation |
| `CONCURRENT_MODIFICATION` | 409 | Stale `RowVersion` on update/confirm/cancel | Conflict (concurrency) |

`INVOICE_NOT_DRAFT`, `INVOICE_NOT_CONFIRMED`, `INVOICE_POSTED_IMMUTABLE`, `INVALID_INVOICE_STATE_TRANSITION`, `INVOICE_PERIOD_CLOSED`, and `INVOICE_DUPLICATE_DOCUMENT_NUMBER` are state conflicts → **409**; the `DefaultErrorCodeToStatusMap` MUST be extended (or an `InvoiceErrorCodeToStatusMap` added) to map these, since none match the default `*_NOT_FOUND`/`*_CONFLICT`/`CONCURRENT_*` patterns. The validation codes → 400.

`INVALID_INVOICE_STATE_TRANSITION` is the Invoice-domain alias surfaced to clients for the workflow engine's generic `INVALID_STATE_TRANSITION` (SDD-INFRA-008 §4); the service translates the engine's failure code to the domain code (mirroring SDD-FIN-002 §4).

**Frontend obligation.** Every code above MUST get a matching `errors.<CODE>` entry in BOTH `frontend/src/shared/i18n/locales/en.ts` and `bg.ts` in the SAME PR as the invoice frontend (SDD-UI-001). Backend-first this batch; recorded for the frontend phase.

## 5. Versioning Notes

`/api/v1/invoices/*` is the v1 surface: `POST` (create draft), `PUT` (update draft), `DELETE` (delete draft), `POST /{id}/confirm`, `POST /{id}/post`, `POST /{id}/cancel`, `GET` (list), `GET /{id}`.

- **v1 — Initial specification (Batch 16).** The `Invoice` aggregate (four document types, `AP`/`AR` direction); `Draft → Confirmed → Posted` (+ `Cancelled`/`Reversed`) via `IWorkflowEngine<Invoice>`; country-aware tax via `ICountryStrategy`; gapless country-formatted numbering at confirm via `ISequenceGenerator`; audit-first → outbox `InvoiceConfirmedEvent` / `InvoiceCancelledEvent`; the event-driven Confirm→Post handshake with the Journal service; Credit/Debit-Note correction of posted invoices; `Confirmed`/`Posted` immutable.
- **`ICountryStrategy` interface growth (breaking to the interface, per SDD-CTRY-001 §5).** This spec adds the tax members (`ApplyTaxRounding`, tax-rate validity/lookup) and `GenerateDocumentNumber` to `ICountryStrategy`, with `BulgariaStrategy` implementing them. Per SDD-CTRY-001 §5 this is a coordinated, additive-per-spec growth; it is recorded here as the owning spec for those members.
- **Deferred (future versions / specs):**
  - **Automatic `Posted → Reversed` transition on a fully-offsetting Credit Note (§2.7).** The workflow keeps `Posted → { Reversed }` legal and the `CorrectsInvoiceId` linkage between a note and its original IS implemented and tested. What is DEFERRED is the automatic full-offset *detection* that would flip the original `Posted → Reversed` when a note fully offsets it — it depends on the deferred CREDIT_NOTE / DEBIT_NOTE posting-rule templates (§7) being seeded so a note can actually post. In v1 a posted original therefore stays `Posted` after a note is issued; the `Reversed` flag is set only by an explicit future follow-up. The shipped tests assert this shipped behaviour (the original stays `Posted`), not the deferred auto-reverse.
  - **Payment / settlement** against the invoice — SDD-PAY-001/-002 (additive; no change to this lifecycle).
  - **VAT journals / НАП export** — SDD-RPT-003 / SDD-INT-NAP-001 (read the posted invoices; no change here).
  - **Optimistic synchronous posting** at confirm — a future `CHG-ENH-*` (§2.5) if required; the event-driven path is v1.
  - **FX rate resolution** — SDD-FIN-005.
  - **Approval state** between `Draft` and `Confirmed` — a future `CHG-FEAT-*` (new state + `AllowedNextStates` + migration per SDD-INFRA-008 §5).
- Adding an event field is additive; changing the state set/transition semantics or a sequence format pattern is breaking and requires `/api/v2/` + a `CHG-ENH-*` (+ an enum migration / НАП-stability review for number formats).

## 6. Test Plan

> Environment: Docker/SQL/Redis/RabbitMQ are not available offline — only `[Unit]` tests run by default. EF unit tests use SQLite in-memory; the workflow engine, tax computation, totals reconciliation, number assignment, and outbox publish are testable without a real broker (the publish is asserted via the MassTransit in-memory test harness; the gapless number via SQLite + a faked `ISequenceGenerator`; tax via a faked `ICountryStrategy`). `WebApplicationFactory` HTTP tests + real-SQL/outbox/back-event-consumer tests carry `[Category("Integration")]` and are excluded from the default run. All business tests MUST reference `[Category("SDD-INV-001")]`.

### 6.1 State machine & guards (Unit)

| Test name | Kind |
|---|---|
| `Confirm_DraftInvoice_TransitionsToConfirmed` | [Unit] |
| `Confirm_NonDraftInvoice_ReturnsInvoiceNotDraft` | [Unit] |
| `Confirm_DraftWithNoLines_ReturnsInvoiceLinesRequired_NoNumberAllocated` | [Unit] |
| `Confirm_MismatchedTotals_ReturnsInvoiceTotalsMismatch_NoNumberAllocated` | [Unit] |
| `Confirm_ClosedPeriod_ReturnsInvoicePeriodClosed_WhenGuardRejects` | [Unit] |
| `Confirm_WithDefaultAlwaysOpenGuard_Succeeds` | [Unit] |
| `Cancel_DraftInvoice_TransitionsToCancelled` | [Unit] |
| `Cancel_ConfirmedInvoice_TransitionsToCancelled_KeepsDocumentNumber` | [Unit] |
| `Cancel_PostedInvoice_ReturnsInvalidInvoiceStateTransition` | [Unit] |
| `Cancel_WithoutReason_ReturnsInvoiceCancelReasonRequired` | [Unit] |
| `Workflow_DraftAllowsConfirmedAndCancelled_ConfirmedAllowsPostedAndCancelled_PostedAllowsReversed` | [Unit] |
| `Update_ConfirmedInvoice_ReturnsInvoicePostedImmutable` | [Unit] |
| `Delete_ConfirmedInvoice_ReturnsInvoicePostedImmutable` | [Unit] |
| `Delete_DraftInvoice_RemovesInvoice` | [Unit] |

### 6.2 Confirm side effects & posting handshake (Unit — SQLite in-memory + MassTransit test harness)

| Test name | Kind |
|---|---|
| `Confirm_AssignsGaplessDocumentNumber_FromSequenceGenerator_PerDocumentType` | [Unit] |
| `Confirm_FormatsDocumentNumber_ViaCountryStrategy` | [Unit] |
| `Confirm_StampsConfirmedAtAndConfirmedBy` | [Unit] |
| `Confirm_RecordsAuditStateChange_BeforeOutboxPublish` | [Unit] |
| `Confirm_PublishesInvoiceConfirmedEvent_WithPostingRuleKeyAndTotals` | [Unit] |
| `Confirm_AppendsStatusHistoryRow_DraftToConfirmed` | [Unit] |
| `Confirm_DoesNotPublishEvent_WhenGuardFails` | [Unit] |
| `InvoicePostedConsumer_LinksJournalEntryId_AndTransitionsConfirmedToPosted` | [Unit] |
| `InvoicePostedConsumer_DuplicateEvent_IsNoOp_WhenAlreadyPosted` | [Unit] |
| `InvoiceConfirmedConsumer_PostsJournalEntry_AndPublishesInvoicePostedEvent` | [Unit] — Journal-side |
| `Cancel_PublishesInvoiceCancelledEvent_WithReason` | [Unit] |

### 6.3 Tax computation & totals (Unit — faked `ICountryStrategy`)

| Test name | Kind |
|---|---|
| `ComputeTotals_LineNetTaxGross_UseCountryStrategyRounding` | [Unit] |
| `ComputeTotals_HeaderTotals_AreSumOfLineComponents` | [Unit] |
| `ComputeTotals_NetPlusTaxEqualsGross_ToTheCent` | [Unit] |
| `Validate_MismatchedLineSums_ReturnsInvoiceTotalsMismatch` | [Unit] |
| `Validate_NegativeTaxRate_ReturnsInvalidInvoiceTaxRate` | [Unit] |
| `ComputeTotals_UsesDecimalArithmetic_NoFloatingPoint` | [Unit] |

### 6.4 Create / list / get / validation (Unit)

| Test name | Kind |
|---|---|
| `CreateDraft_ManualValidInvoice_PersistsInDraft_WithNullDocumentNumber` | [Unit] |
| `CreateDraft_DerivesDirectionFromDocumentType` | [Unit] |
| `CreateDraft_SetsBaseCurrencyFromCountryStrategy` | [Unit] |
| `CreateDraft_RecordsAuditCreate` | [Unit] |
| `CreateDraft_ManualWithNoLines_ReturnsInvoiceLinesRequired` | [Unit] |
| `UpdateDraft_StaleRowVersion_ReturnsConcurrentModification` | [Unit] |
| `Get_ReturnsNotFound_WhenInvoiceDoesNotExist` | [Unit] |
| `Search_ReturnsPagedResultOrderedByIssueDateDescending` | [Unit] |
| `Search_DoesNotCacheTransactionalData` | [Unit] |
| `InvoiceErrorCodes_DefinesPeriodClosed_ForDeferredFin004Seam` | [Unit] |

### 6.5 Credit/Debit-Note correction (Unit)

> **Note (shipped vs deferred — §5/§7).** The `CorrectsInvoiceId` linkage IS implemented and tested. The automatic full-offset `Posted → Reversed` transition is DEFERRED (it depends on the deferred CREDIT_NOTE / DEBIT_NOTE posting-rule templates, §7), so in v1 the original stays `Posted` after a note is issued. The two test names below therefore document the SHIPPED behaviour: `CreditNote_FullyOffsetsOriginal_TransitionsOriginalToReversed` and `CreditNote_PartialOffset_LeavesOriginalPosted` both assert that the original remains `Posted` (the linkage is recorded; the auto-reverse is not yet performed), NOT that a full-offset note auto-reverses the original.

| Test name | Kind |
|---|---|
| `CreditNote_LinksToOriginalViaCorrectsInvoiceId` | [Unit] |
| `CreditNote_FullyOffsetsOriginal_TransitionsOriginalToReversed` | [Unit] — asserts shipped behaviour: original stays `Posted` (auto-reverse deferred) |
| `CreditNote_PartialOffset_LeavesOriginalPosted` | [Unit] |
| `Correction_DoesNotMutateOriginalLinesOrNumber` | [Unit] |

### 6.6 Endpoint & wiring (Integration — `[Category("Integration")]`, excluded from the fast offline run)

> Run against the shared Testcontainers harness (`src/Tests/Finance.IntegrationTesting` — real SQL Server + Redis + RabbitMQ, minted JWT + real RBAC).

| Test name | Kind |
|---|---|
| `Create_Returns201_AndPersistsDraft` | [Integration] |
| `Confirm_Returns200_AndWritesOutboxAndAuditRow_InSameTransaction` | [Integration] |
| `Confirm_AllocatesGaplessDocumentNumbers_NoGaps_PerDocumentType` | [Integration] — also `[Category("SDD-INFRA-003")]` |
| `Confirm_Returns409_WhenAlreadyConfirmed` | [Integration] |
| `Cancel_Returns400_WhenReasonMissing` | [Integration] |
| `Update_Returns409_WhenInvoiceConfirmed` | [Integration] |
| `PostingHandshake_ConfirmThenJournalPostedBack_MovesInvoiceToPosted_AndLinksJournalEntryId` | [Integration] |
| `Endpoint_Returns403_WhenPermissionMissing` | [Integration] |

## 7. Resolved Decisions & Open Items

### Resolved
- **One aggregate, four document types.** `Invoice` discriminated by `DocumentType` (`PurchaseInvoice`/`SaleInvoice`/`CreditNote`/`DebitNote`) + `Direction` (`AP`/`AR`); header `invoices` + child `invoice_lines`. `invoices.id` = `UNIQUEIDENTIFIER`/`NEWSEQUENTIALID()` (exposed via events), `correlation_id` column, `DECIMAL(18,2)` amounts / `DECIMAL(18,6)` rates, `DATETIMEOFFSET` timestamps, outbox tables in the same DB (Plan Appendix A).
- **State set.** `Draft → Confirmed → Posted`; `Cancelled` from `Draft`/`Confirmed`; `Posted → Reversed` via a fully-offsetting Credit Note. `Cancelled`/`Reversed` terminal. Via `IWorkflowEngine<Invoice>` (SDD-INFRA-008).
- **Numbering at confirm.** Gapless `PINV`/`SINV`/`CN`/`DN` number via `ISequenceGenerator`, formatted by `ICountryStrategy.GenerateDocumentNumber`. Draft has NULL `DocumentNumber`; a cancelled confirmed invoice keeps (never recycles) its number.
- **Posting handshake (chosen): event-driven with a dedicated back-event.** Confirm publishes `InvoiceConfirmedEvent` via the outbox; the Journal service consumes it, posts the JE (SDD-FIN-006), and publishes back the dedicated `InvoicePostedEvent` (NOT the generic `JournalEntryPostedEvent`, which is multi-purpose and already consumed by EventLog); the Invoice service's idempotent consumer links `JournalEntryId` and moves `Confirmed → Posted`. The Invoice service never posts a JE itself. Optimistic synchronous posting is the documented, deferred alternative (§2.5).
- **Tax via the country strategy.** Line/document totals + rounding via `ICountryStrategy`; core stays country-agnostic; `decimal` only.
- **Corrections via notes.** Posted invoices are immutable; corrected only by Credit/Debit Notes linked via `CorrectsInvoiceId` — mirroring SDD-FIN-002 reversal.
- **No caching.** Invoices are transactional data (SDD-INFRA-004).

### Resolved by the Pass-A implementator (Batch 16)
- **Back-event type — RESOLVED: dedicated `InvoicePostedEvent`.** The Journal service publishes a dedicated `InvoicePostedEvent` (`InvoiceId`, `JournalEntryId`, `JournalEntryNumber`) through its outbox after posting, NOT the generic `JournalEntryPostedEvent`. The generic event is published for every journal entry and is already consumed by EventLog; a dedicated event keeps the invoice→posting correlation explicit and avoids fragile multi-purpose routing/matching. The event record lives in `Finance.ServiceModel/Events/Invoices/` (it is part of the invoice posting contract) even though the Journal service publishes it.
- **Period-open guard source — RESOLVED for v1: default always-open.** The Invoice service registers `IInvoicePeriodGuard` → `AlwaysOpenInvoicePeriodGuard` (mirroring the Journal `AlwaysOpenPostingPeriodGuard` seam), run as an `IChainValidator<WorkflowContext<Invoice>>` on the `Draft → Confirmed` transition. When SDD-FIN-004's lookup is reachable from this service, a gateway-backed guard replaces only that DI registration and returns `INVOICE_PERIOD_CLOSED`.
- **`post` endpoint vs back-event — RESOLVED: endpoint exposed in v1.** `POST /{id}/post` is exposed as the manual-completion/visibility seam (`finance.invoice:post`); the asynchronous `InvoicePostedEvent` consumer is the primary path. If the JE is already linked the endpoint confirms `Posted`; otherwise it returns `INVOICE_NOT_CONFIRMED` (posting-pending).
- **`PostingRuleKey` mapping — RESOLVED.** `InvoiceDocumentTypeMap` in the Invoice service maps `SaleInvoice → SALE_INVOICE`, `PurchaseInvoice → PURCHASE_INVOICE`, `CreditNote → CREDIT_NOTE`, `DebitNote → DEBIT_NOTE`; the key is carried on `InvoiceConfirmedEvent`. The BG strategy currently seeds `SALE_INVOICE` and `PURCHASE_INVOICE`; `CREDIT_NOTE` / `DEBIT_NOTE` rule templates MUST be added to the seeded posting-rule store (SDD-FIN-006 / SDD-CTRY-001) before note posting is enabled (Deferred to the note-posting follow-up).
- **`ICountryStrategy` tax/number members shape — RESOLVED.** Grown with `decimal ApplyTaxRounding(decimal amount)`, `bool IsValidTaxRate(decimal rate)`, and `string GenerateDocumentNumber(InvoiceDocumentType documentType, long sequenceValue)`; `BulgariaStrategy` implements them (BGN, recognized rates 20% / 9% / 0%, `MidpointRounding.AwayFromZero` to 2 dp, BG prefixes ФПок/ФПр/КИ/ДИ). A new additive `ISequenceGenerator.NextValueAsync` returns the raw gapless counter so the country owns the number format.

### Open / deferred (later passes)
- **Counterparty validation/enrichment.** `CounterpartyId` is a Warehouse GUID (no FK). v1 stores it opaquely; enrich lazily for display via the Warehouse Refit client (SDD-INT-WH-002) in a later pass.
- **Warehouse inbound consumers.** The document-triggered draft creation from Warehouse events (GoodsReceiptCompleted, etc.) is SDD-INT-WH-001 (Pass B). The Pass-A create path (`IInvoiceService.CreateDraftAsync(..., allowEmptyLines, ...)`) is reusable by those consumers.
