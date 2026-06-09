# SDD-INFRA-006 — Resilient Message Publisher (MassTransit + Outbox + Idempotency)

> Status: Implemented (library: `AddFinanceMessageBus<TDbContext>`, `IdempotencyFilter<T>`, `UseFinanceIdempotency`, outbox/RabbitMQ/retry wiring; `IFinanceEvent` marker in `Finance.ServiceModel/Events/`). Batch 16 (SDD-INV-001 / SDD-INT-WH-001) added an `AddFinanceMessageBus<TDbContext>(services, config, Action<IBusRegistrationConfigurator> configureConsumers)` overload so a PUBLISHING service can ALSO register consumers alongside its EF outbox. Deferred: per-service outbox tables/migrations (Batch 4+, landed per publishing service as they ship); remaining concrete domain events (later batches).
> Owner: Platform
> Last updated: 2026-06-10
> Category: Infrastructure
> Related: SDD-INFRA-001, SDD-INFRA-004 (reuses its Redis `IConnectionMultiplexer` for idempotency), SDD-EVTLOG-001, SDD-INT-WH-001, SDD-AUDIT-001
> Mirrors: Warehouse `Warehouse.Infrastructure.Messaging`
> Implementation: `src/Infrastructure/Messaging/Finance.Infrastructure.Messaging/` (references `Finance.Common` and `Finance.Infrastructure.Caching`); marker interface in `src/Finance.ServiceModel/Events/`
> Build order: RUNS AFTER SDD-INFRA-004 (Caching) — depends on its Redis `IConnectionMultiplexer` registration.

---

## 1. Context & Scope

This spec defines `Finance.Infrastructure.Messaging`, the standard contract for **reliable** publishing and consumption of domain events between Finance microservices and Warehouse. Finance is **stricter than Warehouse** here: Warehouse uses fire-and-forget MassTransit publishing wrapped in try/catch, but in Finance an unpublished `JournalEntryPostedEvent` could mean a missing audit row in `EventLog`, which is unacceptable. Finance therefore uses MassTransit's **EF Core Transactional Outbox**: the event row is written to `OutboxMessage` inside the same DB transaction as the business entity; a background delivery service publishes it to RabbitMQ after the commit.

Every consumer is wrapped in `IdempotencyFilter<T>` using Redis `SETNX` keyed by `MessageId` (7-day TTL) so retries and DLQ replays cannot double-post. The Redis connection is the `IConnectionMultiplexer` registered by `Finance.Infrastructure.Caching` (SDD-INFRA-004) — this library does **not** register its own.

### Batch-3 resolved decisions

- **Library location:** `src/Infrastructure/Messaging/Finance.Infrastructure.Messaging/`, references `Finance.Common` **and** `Finance.Infrastructure.Caching` (for the shared Redis `IConnectionMultiplexer`). Built with `dotnet build` on its own `.csproj`; it does **not** add itself to `src/Finance.slnx` (the Integrate step does that).
- **Build order:** RUNS AFTER SDD-INFRA-004 (Caching). The idempotency filter reuses the Caching library's Redis `IConnectionMultiplexer`.
- **Event convention — `IFinanceEvent`:** this batch adds a single base marker interface `IFinanceEvent { Guid MessageId; string CorrelationId; DateTimeOffset OccurredAt; }` to `src/Finance.ServiceModel/Events/`. Concrete `sealed record` events (Account, Currency, JournalEntry…) implement it and arrive in **later batches**. This `IFinanceEvent` marker is the **ONLY** edit to `Finance.ServiceModel` in this batch.
- **Deferred — per-service outbox tables:** the `OutboxMessage` / `OutboxState` / `InboxState` EF tables + migrations land in each **publishing service DbContext** later (Batch 4+). The library ships the MassTransit + outbox **wiring only**.
- **Ships now (Status Active):** `AddFinanceMessageBus<TDbContext>(config)`, the `IdempotencyFilter<T>` + `bus.UseFinanceIdempotency()`, retry/DLQ policy, RabbitMQ host wiring, the `IFinanceEvent` marker, and the `MassTransitTestHarness` registration helper.

**In scope:**
- DI extension `services.AddFinanceMessageBus<TDbContext>(configuration)`
- Per-DbContext outbox **wiring** via `cfg.AddEntityFrameworkOutbox<TDbContext>(...)` with `UseSqlServer()` + `UseBusOutbox()`, `QueryDelay = 1 s`, `DuplicateDetectionWindow = 30 min`
- `MessageRetry` policy: 1 s → 5 s → 15 s, then dead-letter `<queue>_error`
- `IdempotencyFilter<T>` consume filter (Redis SETNX over the Caching library's multiplexer, 7-day TTL) + `bus.UseFinanceIdempotency()`
- Event convention: `IFinanceEvent` marker interface in `Finance.ServiceModel/Events/`; concrete events are `sealed record` + `required` properties + `CorrelationId` + `MessageId`
- Domain-event namespace convention: `Finance.ServiceModel/Events/<Domain>/`
- `MassTransitTestHarness` registration helper for test projects

**Out of scope:**
- Direct (sync) HTTP calls between services — those use Refit + Polly, not the bus
- Saga state machines — defer to a future spec when first multi-step flow appears (payment-allocation cancellation is the likely first candidate)
- Schema registry — JSON contracts via `Finance.ServiceModel` package versioning is sufficient for now
- **The physical outbox tables (`OutboxMessage` / `OutboxState` / `InboxState`) + EF migrations** — owned by each publishing service DbContext (Batch 4+); the library ships wiring only
- **Concrete domain-event records** (Account, Currency, JournalEntry…) — arrive in later batches; this batch ships only the `IFinanceEvent` marker
- Redis `IConnectionMultiplexer` registration — owned by `Finance.Infrastructure.Caching` (SDD-INFRA-004); this library reuses it

## 2. Behavior

### 2.1 Outbox configuration (MUST)
The library ships the outbox **wiring**; the physical tables + migration are added per publishing service DbContext later (Batch 4+). Every Finance microservice that publishes events MUST:
1. Add `OutboxMessage`, `OutboxState`, `InboxState` EF migrations to its DBModel project. **(Deferred to Batch 4+ — the library does not own any migration.)**
2. Call `services.AddFinanceMessageBus<TDbContext>(configuration)`, which registers `cfg.AddEntityFrameworkOutbox<TDbContext>(o => { o.UseSqlServer(); o.UseBusOutbox(); o.QueryDelay = TimeSpan.FromSeconds(1); o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30); });`
3. Resolve the publishing endpoint as `IPublishEndpoint` (NOT `IBus`) — `AddEntityFrameworkOutbox` rebinds it to write into the outbox table.
4. Publish events inside the same transaction as the entity change. **No try/catch around `Publish` — the outbox guarantees atomicity.**

### 2.2 Event-record convention (MUST)
This batch ships the base marker interface in `Finance.ServiceModel/Events/` (the **only** edit to `Finance.ServiceModel` in Batch 3). Concrete events arrive in later batches.
```csharp
public interface IFinanceEvent
{
    Guid MessageId { get; }
    string CorrelationId { get; }
    DateTimeOffset OccurredAt { get; }
}
```
Concrete events MUST implement `IFinanceEvent` and follow this shape (example for a later batch):
```csharp
public sealed record JournalEntryPostedEvent : IFinanceEvent
{
    public required Guid MessageId { get; init; }
    public required string CorrelationId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required Guid JournalEntryId { get; init; }
    public required string EntryNumber { get; init; }
    public required int FiscalPeriodId { get; init; }
    public required IReadOnlyList<JournalEntryLineDto> Lines { get; init; }
}
```
- Every concrete event MUST implement `IFinanceEvent`.
- `CorrelationId` MUST come from `ICorrelationIdAccessor.Get()`.
- `MessageId` MUST be `Guid.NewGuid()` at construction time.
- `OccurredAt` MUST be `DateTimeOffset.UtcNow`.
- The record MUST be `sealed` so consumers can rely on its concrete shape.

### 2.3 Consumer convention (MUST)
- Consumers live in `<Service>.API/Consumers/`.
- Every consumer MUST be registered with `IdempotencyFilter<T>` via the configurator helper `bus.UseFinanceIdempotency()` so duplicate `MessageId`s are skipped.
- Consumers MUST log entry/exit with the inbound `CorrelationId` pushed onto the NLog scope.
- Consumers MUST NOT throw on permanent business failures — they SHOULD log and acknowledge so the message does NOT poison the queue. (Retryable infrastructure failures — DB connection lost — DO throw so MassTransit retries.)

### 2.4 Retry & DLQ (MUST)
- `bus.UseMessageRetry(r => r.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)))`
- After retries are exhausted, the message moves to `<queue>_error`. Operators get a Grafana alert on any non-zero DLQ depth.
- A replay tool (manual, future) re-queues messages from `<queue>_error` after the underlying issue is fixed.

### 2.5 Idempotency filter (MUST)
The `_redis` `IConnectionMultiplexer` injected below is the instance registered by `Finance.Infrastructure.Caching` (SDD-INFRA-004) — this library MUST NOT register its own.
```csharp
public sealed class IdempotencyFilter<T> : IFilter<ConsumeContext<T>> where T : class
{
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        IDatabase db = _redis.GetDatabase();
        string key = $"finance:processed:{context.MessageId}";
        bool isNew = await db.StringSetAsync(key, "1", TimeSpan.FromDays(7), When.NotExists);
        if (!isNew)
        {
            _logger.LogWarning("Duplicate message {MessageId} skipped", context.MessageId);
            return;
        }
        await next.Send(context);
    }
}
```

### 2.6 Outbox cleanup (SHOULD)
- MassTransit's built-in delivered-message purge MUST be enabled (`o.RemoveDeliveredMessages()` after 7 days).
- A weekly Grafana panel SHOULD monitor outbox row count per service.

## 3. Validation Rules

- Startup MUST fail if `RabbitMQ:Host` is missing; readiness MUST report 503 if RabbitMQ is unreachable on `/health/ready`.
- Startup MUST fail if `ConnectionStrings:Redis` is missing (idempotency filter depends on the Caching library's multiplexer).
- Every concrete event record MUST implement `IFinanceEvent` and carry the `required` modifier on `MessageId`, `CorrelationId`, and `OccurredAt`.
- The library MUST provide a `MassTransitTestHarness` registration helper so consumers/publishers can be exercised in-memory without RabbitMQ.

## 4. Error Rules

| Code | HTTP | Trigger |
|---|---|---|
| `RABBITMQ_UNREACHABLE` | 503 (at /health/ready) | Broker down |
| `DUPLICATE_MESSAGE_SKIPPED` | (logged, not returned) | Idempotency filter caught a replay |
| `MESSAGE_DEAD_LETTERED` | (alert) | Consumer exhausted retries |
| `OUTBOX_GROWTH_ALERT` | (alert) | Outbox row count > 10 000 for > 5 min |

## 5. Versioning Notes

v1: JSON contracts; event-record version implied by package version of `Finance.ServiceModel`. Breaking schema change (renaming a property, removing a required field) MUST publish on a new topic name (e.g., `JournalEntryPostedEvent_v2`) and consumers MUST be upgraded before the old topic is retired.

- **Batch 16 (SDD-INV-001 / SDD-INT-WH-001) — additive overload.** `AddFinanceMessageBus<TDbContext>(services, config, Action<IBusRegistrationConfigurator> configureConsumers)` lets a service that both PUBLISHES (EF outbox on its `TDbContext`) AND CONSUMES register its consumers in the same call as the outbox wiring — the `configureConsumers` callback runs against the `IBusRegistrationConfigurator` before the RabbitMQ host is configured. Used by `Finance.Invoices.API` (publishes `InvoiceConfirmedEvent`/`InvoiceCancelledEvent` + consumes the four Warehouse inbound events and `InvoicePostedEvent`) and by the Journal-side `InvoiceConfirmedEventConsumer`. This is additive — the existing parameterless-consumer `AddFinanceMessageBus<TDbContext>(config)` overload is unchanged; consume-only services keep using `AddEventLogConsumers`-style registration (SDD-EVTLOG-001).

## 6. Test Plan

Batch-3 unit tests live in `src/Infrastructure/Finance.Infrastructure.Tests`. Tests exercising the in-memory `MassTransitTestHarness` (no broker required) run by default as `[Unit]`. Tests requiring a real RabbitMQ broker, real Redis, or real SQL Server outbox tables are `[Category("Integration")]` and excluded from the default offline run.

| Test | Kind |
|---|---|
| `EventRecord_ImplementsIFinanceEvent_AndExposesMessageIdCorrelationIdOccurredAt` | [Unit] |
| `IdempotencyFilter_SkipsDuplicateMessageId` | [Unit] (mocked multiplexer / test harness) |
| `IdempotencyFilter_ProcessesFirstOccurrence` | [Unit] (mocked multiplexer / test harness) |
| `Consumer_ProcessesEventOnce_ViaTestHarness` | [Unit] (MassTransitTestHarness) |
| `Configuration_FailsStartup_WhenRabbitMqHostMissing` | [Unit] |
| `Configuration_FailsStartup_WhenRedisConnectionMissing` | [Unit] |
| `Publish_WritesToOutbox_AndIsNotDispatched_BeforeCommit` | [Integration] (real SQL outbox) |
| `Publish_DispatchesAfterCommit_WithinQueryDelay` | [Integration] (real SQL outbox + RabbitMQ) |
| `Consumer_SkipsDuplicateMessageId_ViaIdempotencyFilter` | [Integration] (real Redis) |
| `Consumer_RetriesOnTransientException` | [Integration] (real RabbitMQ) |
| `Consumer_DeadLettersAfterRetriesExhausted` | [Integration] (real RabbitMQ) |
| `HealthReady_Returns503_WhenRabbitMqDown` | [Integration] |

## 7. Open Items

- Schema registry vs `Finance.ServiceModel` package versioning. v1 sticks with package versioning.
- Cross-vhost federation between Finance and an external customer ERP. Not needed for v1 — they call the Gateway.
