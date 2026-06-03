# SDD-EVTLOG-001 — Centralized Event Log Service

> Status: Implemented (Batch 6 — operational event archive; consumes the 6 existing Finance events only)
> Owner: Platform
> Last updated: 2026-05-30
> Category: Infrastructure
> Related: SDD-INFRA-005, SDD-INFRA-006, SDD-INFRA-009, SDD-OBS-001, SDD-AUDIT-001, SDD-INT-AUTH-001, SDD-INT-WH-001
> Mirrors: Warehouse `SDD-EVTLOG-001`
> ISA-95: Level 4 (Business Planning & Logistics). The event log is a **cross-cutting operational observability record** — a chronological archive of inter-service messages. It is NOT an ISA-95 business transaction or production record; individual `EventLogEntry` rows are not classified entities, they are immutable observability artifacts of the events emitted by classified aggregates (Account, Currency, …).

---

## 1. Context & Scope

This spec defines `Finance.EventLog.API` (port 6008), backed by `Finance.EventLog.DBModel` (`EventLogDbContext`, database `finance_eventlog`, schema `eventlog`). It is the central **operational** archive of MassTransit domain events consumed across the Finance microservices. It answers questions like "did the Accounts service publish an `AccountCreatedEvent` for account 1042?" and "what time did EventLog consume it, and under which correlation id?".

EventLog is the **operational** event archive — it is NOT the legal audit trail. The legal audit trail is `audit.OperationsEvents` (SDD-AUDIT-001). To avoid confusion the EventLog entity is named **`EventLogEntry`** (NOT `OperationsEvent`); it fulfils the role described in earlier drafts as the "OperationsEvent row" while remaining a distinct concept in a distinct schema (`eventlog` vs `audit`).

Because EventLog only **archives** events that classified aggregates already audited at source, EventLog itself is **exempt from SDD-AUDIT-001 writes** — consuming an event MUST NOT produce an `audit.OperationsEvents` row. Writing audit rows here would double-count and break the single-writer audit model.

**In scope (Batch 6 — shipping):**
- `Finance.EventLog.API` microservice (port 6008) + `Finance.EventLog.DBModel` (`EventLogDbContext`, database `finance_eventlog`, schema `eventlog`, design-time factory, one `InitialCreate` migration)
- `EventLogEntry` append-only entity in schema `eventlog`
- One MassTransit consumer per **existing Finance event type** — the 6 events already defined in `Finance.ServiceModel/Events/`: `AccountCreatedEvent`, `AccountUpdatedEvent`, `AccountDeactivatedEvent`, `CurrencyCreatedEvent`, `CurrencyUpdatedEvent`, `CurrencyDeactivatedEvent` (all `: IFinanceEvent`)
- Per-event-type `IEventMappingStrategy<TEvent>` (Strategy pattern) so a new event type is "register strategy + consumer" only
- Idempotent consumption via `UseFinanceIdempotency()` (Redis `SETNX` keyed by `MessageId`, SDD-INFRA-006)
- `GET /api/v1/events` query endpoint over a `FilterRequest` → `PagedResult<EventLogEntryDto>` (SDD-INFRA-005) with default order `OccurredAt DESC`
- Daily retention background job (`EventLog:RetentionDays`, default 90)
- Gateway `eventlog-cluster` + route (`/api/v1/events` → 6008) and a `/health/ready` aggregation entry

**Out of scope (deferred):**
- **Warehouse inbound events (SDD-INT-WH-001) — DEFERRED.** EventLog does NOT consume any Warehouse-published event in v1; only the 6 Finance events above are consumed. Warehouse subscriptions are drafted and added in a later batch.
- Legal-grade tamper-proof audit (use SDD-AUDIT-001)
- Replaying events into a fresh service (use MassTransit DLQ replay tool)
- Acting on events to drive workflows (consumers in other services do that)
- Per-event `replay` admin action (future v2)
- Cold-tier archival (Parquet on object storage)

## 2. Behavior

### 2.0 EventLogEntry entity (MUST)
- The append-only archive row is the entity **`EventLogEntry`** in schema `eventlog` (NOT `audit`). Naming it `EventLogEntry` keeps it distinct from the SDD-AUDIT-001 `audit.OperationsEvents` legal trail while fulfilling the role earlier drafts called the "OperationsEvent row".
- `EventLogEntry` MUST expose: `Id` (`INT IDENTITY`, table PK), `EventId` (`Guid` — the inbound `MessageId`; indexed and unique), `EventType` (`string`), `SourceService` (`string`), `OccurredAt` (`DATETIMEOFFSET`), `ReceivedAt` (`DATETIMEOFFSET`, `SYSDATETIMEOFFSET()` default), `CorrelationId` (`string`, indexed), `PayloadJson` (`nvarchar(max)`).
- `EventLogEntry` rows MUST be append-only — never UPDATEd or edited after insert.
- Mapping MUST use EF Core Fluent API only (no Data Annotations). The unique index on `EventId` and the index on `CorrelationId` MUST be configured in the entity configuration.
- `EventLogEntry` MUST mark `EventType`, `SourceService`, `CorrelationId`, and `OccurredAt` as `[Filterable]` and `[Sortable]` for SDD-INFRA-005.

### 2.1 Consumer registration (MUST)
- `Finance.EventLog.API` MUST register exactly one MassTransit consumer per **existing Finance event type**: `AccountCreatedEvent`, `AccountUpdatedEvent`, `AccountDeactivatedEvent`, `CurrencyCreatedEvent`, `CurrencyUpdatedEvent`, `CurrencyDeactivatedEvent` (the 6 events already in `Finance.ServiceModel/Events/{Accounts,Nomenclature}`, all `: IFinanceEvent`). These events MUST be reused — not recreated.
- Warehouse inbound events (SDD-INT-WH-001) MUST NOT be consumed in v1 (deferred).
- The MassTransit bus MUST be configured to **consume only**. EventLog does not publish, so the EF transactional outbox is optional/skipped.
- Each consumer MUST log entry and exit using structured NLog templates that carry the inbound `CorrelationId` on the ambient log scope (SDD-OBS-001).
- Consuming an event MUST NOT write an `audit.OperationsEvents` row — EventLog is exempt from SDD-AUDIT-001 writes (it is the operational archive, not the legal trail).

### 2.2 Mapping strategy (MUST)
- One `IEventMappingStrategy<TEvent>` per event type, registered via `services.AddScoped<IEventMappingStrategy<TEvent>, TEventStrategy>()`.
- Each strategy MUST map the inbound event to an `EventLogEntry` with: `EventId = MessageId`, `EventType`, `SourceService`, `OccurredAt` (from the event), `ReceivedAt = SYSDATETIMEOFFSET()/now`, `CorrelationId`, and `PayloadJson` = `System.Text.Json` serialization of the event.
- Strategies MUST tolerate unknown JSON properties so Warehouse/event schema evolution (an added property) does not break deserialization.
- Adding a new event type MUST be "new strategy class + new consumer + scoped registration" with no change to existing strategies.

### 2.3 Idempotency (MUST)
- Every consumer MUST be wrapped via `UseFinanceIdempotency()` (`IdempotencyFilter<T>` from SDD-INFRA-006, Redis `SETNX` keyed by `MessageId`, 7-day TTL). A replay (retry or DLQ redelivery) of the same `MessageId` MUST NOT produce a duplicate `EventLogEntry` row.
- Because `EventId` is uniquely indexed, a duplicate insert that slips past the idempotency filter MUST fail rather than create a second row.

### 2.4 Query endpoint (MUST)
- `GET /api/v1/events` MUST accept an SDD-INFRA-005 `FilterRequest` (filter/sort/page over `EventType`, `SourceService`, `CorrelationId`, `OccurredAt`) and return `PagedResult<EventLogEntryDto>`.
- The endpoint MUST require permission `finance.event:read` via `[RequirePermission("finance.event:read")]`.
- The service MUST inherit `SearchableServiceBase` and return `Result<T>`; the controller MUST inherit `BaseApiController`.
- `BuildBaseQuery` MUST apply a default order of `OccurredAt DESC` (the filter library appends the PK as the final deterministic sort term per SDD-INFRA-005).

### 2.5 Search by correlation ID (MUST)
- The endpoint MUST support `correlationId={guid}` as a filter that returns all matching `EventLogEntry` rows across event types in chronological order, satisfying the "show me everything that happened in this trace" use case.

### 2.6 By-id lookup (MAY)
- A `GET /api/v1/events/{id}` single-entry lookup MAY be added. If added, a miss MUST return 404 `EVENT_NOT_FOUND`.

### 2.7 Retention (MUST)
- A daily `IHostedService` MUST delete `EventLogEntry` rows whose `OccurredAt` (or `ReceivedAt`) is older than `EventLog:RetentionDays` (default 90).
- The retention job MUST log the count of deleted rows using a structured NLog template.

## 3. Validation Rules

- Date range: when both `from` and `to` are supplied, `from <= to`; otherwise 400 `INVALID_DATE_RANGE`.
- Page size: `pageSize <= 200`, enforced by the SDD-INFRA-005 filter library; otherwise 400 `PAGE_SIZE_TOO_LARGE`.
- Authorization: the caller MUST hold `finance.event:read`; otherwise 403.

## 4. Error Rules

| Code | HTTP | Trigger | Type | Constant source |
|---|---|---|---|---|
| `INVALID_DATE_RANGE` | 400 | `from > to` | Validation | `Finance.Common.ErrorCodes.EventLogErrorCodes.INVALID_DATE_RANGE` |
| `PAGE_SIZE_TOO_LARGE` | 400 | `pageSize > 200` | Validation | `Finance.Common.ErrorCodes.FilterErrorCodes.PAGE_SIZE_TOO_LARGE` |
| `EVENT_NOT_FOUND` | 404 | optional `GET /events/{id}` misses | Not found | `Finance.Common.ErrorCodes.EventLogErrorCodes.EVENT_NOT_FOUND` |
| (RBAC) | 403 | caller lacks `finance.event:read` | Authorization | `Warehouse.Auth.Shared` (SDD-INT-AUTH-001) |

All error responses MUST be ProblemDetails (RFC 7807): `title` = machine code (SCREAMING_SNAKE_CASE), `detail` = developer English, `type` = `https://finance.local/errors/{code}`. `INVALID_DATE_RANGE` and `EVENT_NOT_FOUND` already exist in `Finance.Common.ErrorCodes.EventLogErrorCodes`.

## 5. Versioning Notes

- **v1 (Batch 6, Active)** — Operational event archive. Consumes the 6 existing Finance events (Account/Currency create/update/deactivate) into the append-only `EventLogEntry` table via per-type `IEventMappingStrategy<TEvent>` and idempotent (`UseFinanceIdempotency`) consumers; read-only `GET /api/v1/events` query API (SDD-INFRA-005 `FilterRequest` → `PagedResult<EventLogEntryDto>`, default order `OccurredAt DESC`); daily retention (`EventLog:RetentionDays`, default 90). Warehouse inbound events (SDD-INT-WH-001) deferred. Non-breaking — additive new service.
- **v2 (future)** — Add Warehouse inbound event consumers (SDD-INT-WH-001) and an admin-only per-event `replay` action once replay tooling matures. Non-breaking.

## 6. Test Plan

Consumer tests run on the MassTransit in-memory test harness (no RabbitMQ) with a faked Redis `SETNX` seam; EF tests use SQLite in-memory. Full HTTP/broker/SQL tests are `[Category("Integration")]` and excluded from the default offline run. All business tests are tagged `[Category("SDD-EVTLOG-001")]`. Test project: `src/Infrastructure/EventLog/Finance.EventLog.API.Tests`.

| Test | Kind |
|---|---|
| `ConsumeAsync_AccountCreatedEvent_PersistsEventLogEntry` | [Unit] |
| `ConsumeAsync_ReplayedMessageId_DoesNotDuplicateEntry` | [Unit] |
| `MapToEntry_KnownEvent_SetsEventIdFromMessageId` | [Unit] |
| `MapToEntry_PayloadWithUnknownProperty_DeserializesWithoutError` | [Unit] |
| `SearchAsync_NoSort_OrdersByOccurredAtDescending` | [Unit] |
| `SearchAsync_FilterByEventType_ReturnsOnlyMatchingEntries` | [Unit] |
| `SearchAsync_FilterByCorrelationId_ReturnsAllEntriesInTrace` | [Unit] |
| `SearchAsync_PageSizeOverLimit_ReturnsPageSizeTooLarge` | [Unit] |
| `ValidateRange_FromAfterTo_ReturnsInvalidDateRange` | [Unit] |
| `RunAsync_EntriesOlderThanRetentionDays_DeletesAndLogsCount` | [Unit] |
| `GetEvents_ValidFilter_Returns200PagedResult` | [Integration] |
| `GetEvents_MissingFinanceEventReadPermission_Returns403` | [Integration] |
| `ConsumeAsync_RealBroker_PersistsRowAcrossSqlServer` | [Integration] |

## 7. Open Items

- Warehouse inbound event subscriptions (SDD-INT-WH-001) — deferred to a later batch.
- Per-event admin `replay` action (v2) — deferred until DLQ replay tooling matures.
- Cold-tier archive: Parquet on object storage for events older than 2 years. Decision deferred.
