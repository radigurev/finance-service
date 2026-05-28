# SDD-EVTLOG-001 — Centralized Event Log Service

> Status: Planned
> Owner: Platform
> Last updated: 2026-05-28
> Category: Infrastructure
> Related: SDD-INFRA-006, SDD-AUDIT-001, SDD-INT-WH-001
> Mirrors: Warehouse `SDD-EVTLOG-001`

---

## 1. Context & Scope

This spec defines `Finance.EventLog.API` (port 6008), the central archive of every MassTransit domain event published or consumed by the Finance microservices, plus every Warehouse outbound event Finance consumes. It is an **operational** observability layer (NOT a legal audit trail — see SDD-AUDIT-001 for that). EventLog answers questions like "did the Journal service publish a `JournalEntryPostedEvent` for entry JE-2026-001234?" and "what time did EventLog consume it?".

**In scope:**
- `Finance.EventLog.API` microservice with `EventLogDbContext` and `finance_eventlog` database
- MassTransit consumers for every Finance-published event (one consumer per event type)
- MassTransit consumers for every Warehouse-published event Finance subscribes to
- `OperationsEventFactory` for consistent record construction
- Per-domain event-mapping strategies (Strategy pattern) so a new event type is "register strategy + consumer" only
- `GET /api/v1/events` query endpoint with filtering (event type, correlation id, source service, time range), pagination, search
- Immutable storage (`finance_eventlog.OperationsEvents` is append-only)
- 90-day default retention (configurable); long-term archival to object storage is deferred

**Out of scope:**
- Legal-grade tamper-proof audit (use SDD-AUDIT-001)
- Replaying events into a fresh service (use MassTransit DLQ replay tool)
- Acting on events to drive workflows (consumers in other services do that)

## 2. Behavior

### 2.1 Consumer registration (MUST)
- Every Finance microservice MUST publish via the outbox (SDD-INFRA-006).
- `Finance.EventLog.API` MUST register a consumer for every event type defined in `Finance.ServiceModel/Events/` AND for every Warehouse event Finance subscribes to (via the shared `Warehouse.ServiceModel` package or a copied contract — TBD per SDD-INT-WH-001).

### 2.2 Mapping strategy (MUST)
- One `IEventMappingStrategy<TEvent>` per event type.
- Each strategy MAPs the event payload to an `OperationsEvent` row with: `EventId`, `EventType`, `SourceService`, `OccurredAt`, `ReceivedAt`, `CorrelationId`, `PayloadJson`.
- New event = new strategy class + `services.AddScoped<IEventMappingStrategy<MyEvent>, MyEventStrategy>()` registration.

### 2.3 Idempotency (MUST)
- Consumers MUST use `IdempotencyFilter<T>` from SDD-INFRA-006 keyed by `MessageId`. Replays MUST NOT produce duplicate `OperationsEvents` rows.

### 2.4 Query endpoint (MUST)
- `GET /api/v1/events?eventType=...&correlationId=...&from=...&to=...&page=...&pageSize=...`
- Requires permission `finance.event:read`.
- Response uses the SDD-INFRA-005 paginated envelope (`items`, `totalCount`, `page`, `pageSize`).
- Default sort: `OccurredAt DESC`.

### 2.5 Search by correlation ID (MUST)
- A primary use case is "show me everything that happened in this trace" — the endpoint MUST support `correlationId={guid}` and return all matching rows across event types in chronological order.

### 2.6 Retention (MUST)
- A daily background job MUST delete `OperationsEvents` rows older than `EventLog:RetentionDays` (default 90).
- The deletion job MUST log the count of deleted rows.

## 3. Validation Rules

- `from <= to` if both provided; otherwise 400 `INVALID_DATE_RANGE`.
- `pageSize <= 200`; otherwise 400 `PAGE_SIZE_TOO_LARGE` (SDD-INFRA-005).

## 4. Error Rules

| Code | HTTP | Trigger |
|---|---|---|
| `INVALID_DATE_RANGE` | 400 | `from > to` |
| `EVENT_NOT_FOUND` | 404 | Single-event lookup misses |

Constants live in `Finance.Common.ErrorCodes.EventLogErrorCodes`.

## 5. Versioning Notes

v1: read-only query API. Future v2 may add a per-event `replay` action (admin-only) once the replay tooling is mature.

## 6. Test Plan

| Test | Kind |
|---|---|
| `Consumer_PersistsOperationsEventRow_OnIncomingEvent` | [Integration] |
| `Consumer_DoesNotDuplicate_OnReplay` | [Integration] |
| `Query_FiltersByEventType` | [Integration] |
| `Query_FiltersByCorrelationId` | [Integration] |
| `Query_PaginatesByPageAndPageSize` | [Integration] |
| `Query_SortsByOccurredAtDescendingByDefault` | [Integration] |
| `Retention_DeletesRowsOlderThanThreshold` | [Integration] |
| `Endpoint_Returns403_WhenFinanceEventReadMissing` | [Integration] |

## 7. Open Items

- Cold-tier archive: Parquet on object storage for events older than 2 years. Decision deferred.
- Schema-evolution handling when a Warehouse event adds a property — strategies MUST tolerate unknown JSON properties.
