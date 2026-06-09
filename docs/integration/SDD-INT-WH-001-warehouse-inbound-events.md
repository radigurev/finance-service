# SDD-INT-WH-001 — Warehouse → Finance Inbound Event Subscriptions

> Status: Implemented (Batch 16 — core shipped + tested; 68 unit tests green per the CLAUDE.md §0 lifecycle (`Implemented` = code shipped + tests pass + in force, may carry explicit `Deferred:` notes). The four Invoices-bound MassTransit consumers that turn Warehouse domain events into **draft** Finance invoice documents: `GoodsReceiptCompletedEvent` → draft Purchase Invoice, `ShipmentCompletedEvent` → draft Sale Invoice, `CustomerReturnCompletedEvent` → draft Credit Note, `SupplierReturnShippedEvent` → draft Debit Note. Each consumer is idempotent (Redis `SETNX` on `MessageId` + source-document dedupe) and creates the draft via the SAME domain create path as the manual `POST /api/v1/invoices` (SDD-INV-001) — it does NOT confirm or post. Deferred: counterparty/product enrichment via the Warehouse Refit client (SDD-INT-WH-002).)
> Owner: Finance / Platform
> Last updated: 2026-06-10
> Category: Integration
> Service: `Finance.Invoices.API` — port **6005**, database `finance_invoices` (the consumers live in `Finance.Invoices.API/Consumers/`, SDD-INFRA-006 §2.3)
> Related: SDD-INV-001 (Invoice Lifecycle — the consumers create drafts via the SAME `InvoiceService` create path; they never define a parallel creation flow, never confirm, never post), SDD-INFRA-006 (MassTransit + outbox + `IdempotencyFilter<T>` — every consumer wraps in the idempotency filter; replays from retries/DLQ are safe), SDD-INFRA-004 (Redis — the `SETNX` idempotency store + reference caching; service availability MUST NOT depend on Redis), SDD-INT-AUTH-001 (shared JWT — the consumers run under a service identity; no per-request bearer for bus messages, but S2S enrichment calls to Warehouse use the S2S JWT handler), SDD-INT-WH-002 (Finance → Warehouse Refit — the OUTBOUND counterpart; counterparty/product enrichment, if any, goes through it), SDD-AUDIT-001 (the draft create writes an audit `Create` row — owned by SDD-INV-001), SDD-OBS-001 (structured logging with the inbound `CorrelationId` on the NLog scope; tracing via `traceparent` propagated over the bus), SDD-EVTLOG-001 (the inbound events MAY also be archived)
> ISA-95: Level 4 (Business Planning & Logistics) — cross-domain business-transaction integration (Warehouse Purchasing/Fulfillment → Finance Documents)

---

## 1. Context & Scope

Warehouse and Finance share a single MassTransit + RabbitMQ event mesh (FINANCE-MICROSERVICES-PLAN §7.1). When a logistics event completes in Warehouse — goods received, a shipment dispatched, a customer return processed, a supplier return shipped — Finance must materialize the corresponding **financial document** so the books can later reflect it. This spec defines the **inbound** side of that integration: the four MassTransit consumers that subscribe to Warehouse-owned events and create **draft** Finance invoices.

Four consumers are in scope (the §7.1 rows whose Finance consumer is `Finance.Invoices`):

| Warehouse event (source) | Finance consumer creates | Invoice `DocumentType` |
|---|---|---|
| `GoodsReceiptCompletedEvent` (Purchasing) | a draft **Purchase Invoice** | `PurchaseInvoice` |
| `ShipmentCompletedEvent` (Fulfillment) | a draft **Sale Invoice** | `SaleInvoice` |
| `CustomerReturnCompletedEvent` (Fulfillment) | a draft **Credit Note** | `CreditNote` |
| `SupplierReturnShippedEvent` (Purchasing) | a draft **Debit Note** | `DebitNote` |

Two governing principles:
1. **Drafts only — never confirm, never post.** A consumer's sole job is to create a `Draft` invoice (SDD-INV-001 §2.3) from the Warehouse event's data, via the SAME `InvoiceService` create path the manual endpoint uses. A human (or a later automated step) reviews, completes, and **confirms** the draft — confirmation/posting is the operator's decision and is owned by SDD-INV-001, not by this integration. This prevents an automatic, unreviewed posting from a logistics event.
2. **Idempotent and replay-safe.** RabbitMQ delivers at-least-once; retries and DLQ recovery can redeliver the same message. Every consumer MUST wrap in `IdempotencyFilter<T>` (Redis `SETNX` keyed by `MessageId`, 7-day TTL — SDD-INFRA-006) AND additionally dedupe on the **Warehouse source-document id** (the goods-receipt / shipment / return id) so that even a *distinct* `MessageId` carrying the *same* source document never creates a second draft. A redelivery MUST be a no-op.

**The inbound event contracts are Warehouse-owned.** Finance does NOT define these event records — Warehouse publishes them. This spec defines the **subset of fields Finance depends on** (the consumption contract) so the implementator knows what to read and what to fail on if absent. The full Warehouse event schema is the Warehouse repo's concern; Finance binds to the shared contract assembly / JSON shape and tolerates additional fields it does not use (forward-compatible).

**ISA-95 classification.** These consumers integrate Warehouse Level-4 (or Level-3-originated, Level-4-recorded) logistics business transactions into Finance Level-4 Documents (ISA-95 / IEC 62264 Part 1, §5). The consumer performs a Level-4 document-creation operation (a draft invoice) and emits the immutable audit row via the SDD-INV-001 create path. The Warehouse events themselves are immutable domain events Finance only reads. No Level-3 production activity is modelled inside Finance.

**Scope — covered (v1):**
- The four MassTransit consumers (`GoodsReceiptCompletedConsumer`, `ShipmentCompletedConsumer`, `CustomerReturnCompletedConsumer`, `SupplierReturnShippedConsumer`) in `Finance.Invoices.API/Consumers/`.
- The consumption contract (fields Finance depends on) for each inbound event.
- The mapping from each event to an SDD-INV-001 draft-create request (document type, direction, counterparty, currency, line items).
- Idempotency: `IdempotencyFilter<T>` on `MessageId` + source-document-id dedupe.
- Failure handling: retryable vs permanent; log-and-acknowledge for permanent business failures (SDD-INFRA-006 §2.3).
- Registration wiring (`UseFinanceIdempotency`, retry/DLQ) per SDD-INFRA-006.

**Scope — excluded (DEFERRED / out of scope):**
- **The two non-Invoices §7.1 events** — explicitly deferred, different services, later phases:
  - `ProductionOrderCompletedEvent` (Production) → `Finance.Journal` (post a COGS entry) — OUT OF SCOPE here; owned by a future Journal-side spec (Plan §9 Phase 3/7).
  - `StockMovementRecordedEvent` (Inventory) → `Finance.Reporting` (inventory valuation snapshot feed) — OUT OF SCOPE here; owned by SDD-RPT-* / a future Reporting consumer (Plan §9 Phase 7).
- **Invoice confirmation/posting** — owned by SDD-INV-001. This spec stops at the draft.
- **Outbound Finance → Warehouse calls** (counterparty/product enrichment, reads) — SDD-INT-WH-002.
- **The Warehouse event publishers / full event schemas** — Warehouse repo.
- **Tax computation** of the draft — performed by the SDD-INV-001 create path via `ICountryStrategy`; this spec only supplies the line net/quantity/price/tax-rate inputs from the event.
- **Saga / multi-step correlation** (e.g. matching a later shipment to an earlier order across events) — a future spec when the first multi-step flow appears (SDD-INFRA-006 out-of-scope note).

## 2. Behavior

> **Consumer contract (SDD-INFRA-006 §2.3).** Consumers live in `Finance.Invoices.API/Consumers/`. Each MUST be registered with `IdempotencyFilter<T>` via `bus.UseFinanceIdempotency()`. Each MUST push the inbound `CorrelationId` onto the NLog scope and log entry/exit with structured templates. A consumer MUST create the draft by calling the SDD-INV-001 `InvoiceService` create method — it MUST NOT construct/persist an `Invoice` directly or duplicate validation/audit logic. `CancellationToken` from the consume context MUST be threaded to the service call.

### 2.1 Common consumer behavior (MUST)
- Each of the four consumers MUST, on receiving its event:
  1. Be short-circuited by `IdempotencyFilter<T>` if the `MessageId` was already processed (Redis `SETNX`, 7-day TTL) — a duplicate `MessageId` MUST be skipped without side effects (SDD-INFRA-006).
  2. Additionally check whether a Finance invoice already exists for the **Warehouse source-document id** (`SourceDocumentId` + `SourceDocumentType`); if one exists, the consumer MUST treat the message as already handled and acknowledge **without** creating a second draft (`[Unit]`-testable dedupe). This guards against a redelivery with a *new* `MessageId` for the *same* source document.
  3. Map the event to an SDD-INV-001 draft-create request (§2.3) and call the `InvoiceService` create method, which creates a `Draft` invoice, computes totals via `ICountryStrategy`, and writes the audit `Create` row (all owned by SDD-INV-001 §2.3).
  4. Persist the `SourceDocumentId` + `SourceDocumentType` on the created invoice so the §2.1.2 dedupe is queryable and so the document is traceable back to its Warehouse origin.
  5. Log success with the created invoice id and the source-document id, under the inbound `CorrelationId` scope.
- A consumer MUST NOT confirm or post the draft, MUST NOT publish a Finance event of its own (the draft create publishes nothing — SDD-INV-001 §2.3), and MUST NOT write GL.
- The created draft's `CorrelationId` MUST be the inbound event's `CorrelationId` so the document is traceable end-to-end across the mesh (SDD-INFRA-001).

### 2.2 Per-consumer mapping (MUST)
- `GoodsReceiptCompletedConsumer` MUST create a `Draft` **Purchase Invoice** (`DocumentType = PurchaseInvoice`, `Direction = AP`), counterparty = the supplier id from the event, lines = the received line items (product id, quantity, unit price, tax rate). `SourceDocumentType = "GoodsReceipt"`.
- `ShipmentCompletedConsumer` MUST create a `Draft` **Sale Invoice** (`DocumentType = SaleInvoice`, `Direction = AR`), counterparty = the customer id, lines = the shipped line items. `SourceDocumentType = "Shipment"`.
- `CustomerReturnCompletedConsumer` MUST create a `Draft` **Credit Note** (`DocumentType = CreditNote`), counterparty = the customer id, lines = the returned line items. `SourceDocumentType = "CustomerReturn"`. Where the event references the originating sale (a source invoice / sales order id), the consumer SHOULD populate the SDD-INV-001 `CorrectsInvoiceId`/source linkage if a matching Finance invoice exists; if not, the Credit Note is created standalone (the operator links it on review).
- `SupplierReturnShippedConsumer` MUST create a `Draft` **Debit Note** (`DocumentType = DebitNote`), counterparty = the supplier id, lines = the returned line items. `SourceDocumentType = "SupplierReturn"`.

### 2.3 Inbound consumption contract (the fields Finance depends on) (MUST)
- Finance binds to the shared Warehouse event contract. For ALL four events the consumer MUST be able to read, and MUST fail-with-log-and-acknowledge (§2.4) if absent or invalid:
  - `MessageId` (Guid) — the idempotency key (SDD-INFRA-006).
  - `CorrelationId` (string) — propagated onto the created draft and the NLog scope.
  - `OccurredAt` (DateTimeOffset) — used as a default `IssueDate` if the event supplies none.
  - `SourceDocumentId` (Guid) — the Warehouse goods-receipt / shipment / return id; the §2.1.2 dedupe key.
  - `CounterpartyId` (Guid) — the supplier (purchase/debit) or customer (sale/credit) id; stored opaquely on the invoice (no FK — Plan §8).
  - `CurrencyCode` (string, ISO 4217) — the document currency.
  - `Lines` (collection) — each with `ProductId` (Guid), `Quantity` (decimal > 0), `UnitPrice` (decimal ≥ 0), and `TaxRate` (decimal ≥ 0; if the event omits it, the consumer SHOULD default to the country's standard rate via `ICountryStrategy` — SDD-INV-001 §2.8).
- The consumer MUST tolerate and ignore Warehouse fields it does not consume (forward-compatible — extra fields MUST NOT cause a failure).
- A description per line MAY be carried; if absent the consumer MAY derive a placeholder from the product id (the operator refines on review).

### 2.4 Failure handling (MUST — SDD-INFRA-006 §2.3)
- A **permanent business failure** (e.g. the event has zero usable lines, a malformed/empty `CounterpartyId`, or a currency the system does not recognize) MUST be logged at error with the inbound `CorrelationId` and the `SourceDocumentId`, and the message MUST be **acknowledged** (not thrown) so it does NOT poison the queue or loop through the DLQ. The unprocessable event SHOULD be surfaced for operator attention (log + optional dead-letter), NOT retried indefinitely.
- A **retryable infrastructure failure** (DB connection lost, Redis unreachable for the `SETNX`, the `InvoiceService` returns a transient failure) MUST be allowed to throw so MassTransit applies the retry policy (1s → 5s → 15s, then `<queue>_error` DLQ — SDD-INFRA-006 §2.4). On retry, idempotency + source-document dedupe (§2.1) MUST prevent a double draft.
- Idempotency MUST hold across the retry/DLQ boundary: a message reprocessed after a transient failure MUST NOT create a second draft (the `SETNX` claim is only finalized on success, and the source-document dedupe is the backstop).

### 2.5 Cross-cutting obligations (MUST)
- The consumers MUST run under the service's MassTransit configuration registered by `AddFinanceMessageBus<InvoicesDbContext>` with `UseFinanceIdempotency()` (SDD-INFRA-006). They MUST NOT register a bespoke broker connection.
- If a consumer needs to enrich the draft with Warehouse master data (e.g. a counterparty name for display), it MUST go through the SDD-INT-WH-002 Refit client (`CorrelationIdDelegatingHandler` → `ServiceToServiceJwtHandler` → `AddStandardResilienceHandler`) — NEVER a bespoke `HttpClient`. v1 SHOULD store the counterparty id opaquely and defer enrichment to display time (SDD-INV-001 §7), so a Warehouse outage does NOT block draft creation.
- Tracing: the `traceparent` propagated over the bus MUST be honored and the `CorrelationId` stamped onto `Activity.Current` (SDD-OBS-001). Logging MUST use NLog structured templates (no string interpolation).

### 2.6 Edge cases (MUST)
- **Duplicate `MessageId` (retry/DLQ replay).** The `IdempotencyFilter<T>` MUST skip it — no second draft (`[Unit]` via the MassTransit test harness + a faked Redis store).
- **Distinct `MessageId`, same `SourceDocumentId` (re-publish).** The source-document dedupe (§2.1.2) MUST prevent a second draft even though the idempotency key differs.
- **Event with zero usable lines.** Permanent business failure — log + acknowledge; NO draft, NO infinite retry (§2.4).
- **Unknown currency / malformed counterparty.** Permanent business failure — log + acknowledge.
- **Transient DB/Redis failure mid-consume.** MUST throw → MassTransit retries; on success exactly one draft exists (idempotency holds across the retry).
- **`CustomerReturnCompletedEvent` whose originating sale has no matching Finance invoice.** The Credit Note MUST still be created (standalone); the operator links it on review — the consumer MUST NOT fail because the source invoice is absent.
- **A `ProductionOrderCompletedEvent` or `StockMovementRecordedEvent` arriving at this service.** OUT OF SCOPE — `Finance.Invoices.API` MUST NOT subscribe to them (they belong to Journal / Reporting). No consumer for them exists here.

## 3. Validation Rules

`Finance.Invoices.API` does not own the inbound event schema (Warehouse does), so there is no FluentValidation request surface for the bus messages. Validation is the consumer's contract check on the fields it depends on (§2.3), enforced in code and asserted by tests, plus the SDD-INV-001 create-path validation that runs when the draft is created.

### 3.1 Consumer contract checks (asserted in code + tests)

| Field | Rule | On violation |
|---|---|---|
| `MessageId` | Present (Guid) | Idempotency cannot key — treat as malformed; log + acknowledge |
| `SourceDocumentId` | Present (Guid) | Dedupe cannot key — permanent failure; log + acknowledge |
| `CounterpartyId` | Present, non-empty Guid | Permanent failure; log + acknowledge |
| `CurrencyCode` | ISO 4217 (3 chars) | Permanent failure; log + acknowledge |
| `Lines` | ≥ 1 usable line | Permanent failure (zero-line event); log + acknowledge |
| `Lines[].Quantity` | > 0 | Permanent failure; log + acknowledge |
| `Lines[].UnitPrice` | ≥ 0 | Permanent failure; log + acknowledge |

### 3.2 Delegated validation (SDD-INV-001)

When the consumer calls the `InvoiceService` create path, the SDD-INV-001 §3 validation runs (totals reconciliation, tax-rate validity, currency, dates). A failure there is returned as a `Result.Failure(...)` to the consumer, which MUST treat a business failure as a permanent failure (§2.4) — log + acknowledge — and a transient failure as retryable (throw).

### 3.3 State-based

The consumers are stateless message handlers; the only state rule is the idempotency/dedupe (§2.1). The created invoice's state rules are owned by SDD-INV-001.

## 4. Error Rules

The inbound consumers do NOT produce HTTP responses — they are bus message handlers, so there is no ProblemDetails surface for the consume path. Failures are handled per §2.4 (permanent → log + acknowledge; transient → throw → retry/DLQ). This spec therefore introduces **no new error codes**.

- Business failures surfaced by the delegated SDD-INV-001 create path use that spec's `InvoiceErrorCodes` (e.g. `INVOICE_LINES_REQUIRED`, `INVALID_INVOICE_CURRENCY`) — the consumer LOGS the returned code and acknowledges; it does NOT map it to an HTTP status (there is no HTTP caller).
- Idempotency-skip is NOT an error — it is the designed no-op outcome (logged at debug/info).
- The retry/DLQ behavior and the `<queue>_error` dead-letter are owned by SDD-INFRA-006 §2.4.

(For completeness: when SDD-INT-WH-002 enrichment is invoked and Warehouse is unreachable, that is a transient infrastructure failure handled by the Refit client's resilience handler; v1 avoids it by deferring enrichment, so it does not block draft creation.)

## 5. Versioning Notes

The inbound contract is versioned together with the shared Warehouse event contracts. Finance pins to a compatible contract version and tolerates additive Warehouse fields (forward-compatible consumption — §2.3).

- **v1 — Initial specification (Batch 16).** Four idempotent consumers (`GoodsReceiptCompleted` → Purchase Invoice, `ShipmentCompleted` → Sale Invoice, `CustomerReturnCompleted` → Credit Note, `SupplierReturnShipped` → Debit Note) creating `Draft` invoices via the SDD-INV-001 create path; `IdempotencyFilter<T>` (`MessageId`) + source-document-id dedupe; permanent-vs-retryable failure handling; counterparty stored opaquely (enrichment deferred). `ProductionOrderCompletedEvent` → Journal and `StockMovementRecordedEvent` → Reporting are explicitly OUT OF SCOPE / deferred to later phases.
- **Adding a new inbound consumer** (e.g. the deferred Journal/Reporting consumers, or a new Warehouse event) is additive — a new consumer class + registration; it does NOT change the existing consumers' contracts.
- **A breaking change to a Warehouse event field Finance depends on** (a removed/renamed required field in §2.3) is breaking and requires a coordinated contract version bump + a `CHG-ENH-*`; additive Warehouse fields are non-breaking (ignored).
- **Auto-confirm/auto-post** of the created draft (skipping manual review) would be a behavior change and requires a `CHG-FEAT-*` — v1 deliberately stops at the draft.

## 6. Test Plan

> Environment: the bus + Redis + SQL are not available offline — consumer behavior is `[Unit]`-tested via MassTransit's in-memory test harness, a faked Redis idempotency store, SQLite in-memory for the invoice persistence, and a faked/real `InvoiceService` create path. Real RabbitMQ/Redis/SQL end-to-end consume tests carry `[Category("Integration")]` and are excluded from the default run. All business tests MUST reference `[Category("SDD-INT-WH-001")]`.

### 6.1 Consumer mapping (Unit — MassTransit test harness + SQLite)

| Test name | Kind |
|---|---|
| `GoodsReceiptCompleted_CreatesDraftPurchaseInvoice_WithSupplierAndLines` | [Unit] |
| `ShipmentCompleted_CreatesDraftSaleInvoice_WithCustomerAndLines` | [Unit] |
| `CustomerReturnCompleted_CreatesDraftCreditNote_WithCustomerAndLines` | [Unit] |
| `SupplierReturnShipped_CreatesDraftDebitNote_WithSupplierAndLines` | [Unit] |
| `Consumer_CreatesDraftViaInvoiceServiceCreatePath_NotDirectPersistence` | [Unit] |
| `Consumer_StampsInboundCorrelationId_OnCreatedDraft` | [Unit] |
| `Consumer_PersistsSourceDocumentIdAndType_ForTraceabilityAndDedupe` | [Unit] |
| `Consumer_DoesNotConfirmOrPost_LeavesInvoiceInDraft` | [Unit] |
| `CustomerReturnCompleted_NoMatchingSourceInvoice_CreatesStandaloneCreditNote` | [Unit] |

### 6.2 Idempotency & dedupe (Unit)

| Test name | Kind |
|---|---|
| `Consumer_DuplicateMessageId_IsSkipped_ByIdempotencyFilter` | [Unit] |
| `Consumer_DistinctMessageIdSameSourceDocument_DoesNotCreateSecondDraft` | [Unit] |
| `Consumer_TransientFailureThenRetry_CreatesExactlyOneDraft` | [Unit] |

### 6.3 Failure handling & contract checks (Unit)

| Test name | Kind |
|---|---|
| `Consumer_EventWithZeroLines_LogsAndAcknowledges_NoDraft` | [Unit] |
| `Consumer_MalformedCounterparty_LogsAndAcknowledges_NoDraft` | [Unit] |
| `Consumer_UnknownCurrency_LogsAndAcknowledges_NoDraft` | [Unit] |
| `Consumer_TransientInfrastructureFailure_Throws_ForRetry` | [Unit] |
| `Consumer_BusinessFailureFromCreatePath_AcknowledgesNotThrows` | [Unit] |
| `Consumer_ToleratesUnknownWarehouseFields_DoesNotFail` | [Unit] |

### 6.4 Wiring & end-to-end (Integration — `[Category("Integration")]`, excluded from the fast offline run)

> Run against the shared Testcontainers harness (`src/Tests/Finance.IntegrationTesting` — real SQL Server + Redis + RabbitMQ).

| Test name | Kind |
|---|---|
| `GoodsReceiptCompleted_PublishedToBroker_CreatesExactlyOneDraftPurchaseInvoice` | [Integration] |
| `Consumer_Replay_AfterDlqRecovery_DoesNotDoubleCreate` | [Integration] |
| `Consumers_RegisteredWithIdempotencyFilter_AndRetryPolicy` | [Integration] |
| `OutOfScopeEvents_NotSubscribed_ByInvoicesService` (no consumer for `ProductionOrderCompletedEvent`/`StockMovementRecordedEvent`) | [Integration] |

## 7. Resolved Decisions & Open Items

### Resolved
- **Four Invoices-bound consumers only.** `GoodsReceiptCompleted`/`ShipmentCompleted`/`CustomerReturnCompleted`/`SupplierReturnShipped` → draft Purchase Invoice / Sale Invoice / Credit Note / Debit Note. `ProductionOrderCompletedEvent` (→ Journal) and `StockMovementRecordedEvent` (→ Reporting) are explicitly OUT OF SCOPE / deferred to later phases and other services.
- **Drafts only.** Consumers create `Draft` invoices via the SDD-INV-001 `InvoiceService` create path; they never confirm or post. Confirmation is the operator's reviewed decision (SDD-INV-001).
- **Idempotency twofold.** `IdempotencyFilter<T>` on `MessageId` (SDD-INFRA-006) PLUS source-document-id dedupe — a redelivery with a new `MessageId` for the same source document still creates only one draft.
- **Failure policy.** Permanent business failure → log + acknowledge (no queue poisoning); transient infrastructure failure → throw → MassTransit retry/DLQ. Idempotency holds across the boundary.
- **Counterparty opaque in v1.** `CounterpartyId` stored as the Warehouse GUID (no FK, no blocking enrichment); display-time enrichment via SDD-INT-WH-002 is deferred so a Warehouse outage never blocks draft creation.
- **No new error codes / no HTTP surface.** The consumers are bus handlers; business failures reuse SDD-INV-001's `InvoiceErrorCodes` for logging only.

### Open / deferred (for the Phase-2 implementator)
- **Exact Warehouse event shapes.** §2.3 lists the fields Finance depends on; the implementator MUST bind to the actual shared Warehouse contract assembly / JSON and confirm field names and the line-collection shape with the Warehouse repo. Tolerate additive fields.
- **Source-document dedupe storage.** Whether the `(SourceDocumentType, SourceDocumentId)` dedupe is a unique index on `invoices` or a separate inbox-projection table — implementator decides; a unique filtered index on the invoice columns is the simplest and is recommended.
- **Default tax rate when the event omits one.** Whether to default to `ICountryStrategy`'s standard rate or to leave the draft for operator completion — recommend defaulting to the standard rate and flagging the draft for review.
- **Credit-Note → source-invoice linkage.** Auto-linking a `CustomerReturnCompletedEvent` to an existing Finance sale invoice (`CorrectsInvoiceId`) when a match is found; standalone otherwise. Confirm the match key (Warehouse sales-order id ↔ Finance source-document linkage) with SDD-INV-001.
- **Archival.** Whether the inbound Warehouse events are also archived to the EventLog (SDD-EVTLOG-001) in addition to creating the draft — recommend yes for auditability; confirm with SDD-EVTLOG-001.
