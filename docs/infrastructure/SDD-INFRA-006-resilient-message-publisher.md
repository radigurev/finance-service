# SDD-INFRA-006 — Resilient Message Publisher (MassTransit + Outbox + Idempotency)

> Status: Planned
> Owner: Platform
> Last updated: 2026-05-28
> Category: Infrastructure
> Related: SDD-INFRA-001, SDD-EVTLOG-001, SDD-INT-WH-001, SDD-AUDIT-001
> Mirrors: Warehouse `Warehouse.Infrastructure.Messaging`

---

## 1. Context & Scope

This spec defines `Finance.Infrastructure.Messaging`, the standard contract for **reliable** publishing and consumption of domain events between Finance microservices and Warehouse. Finance is **stricter than Warehouse** here: Warehouse uses fire-and-forget MassTransit publishing wrapped in try/catch, but in Finance an unpublished `JournalEntryPostedEvent` could mean a missing audit row in `EventLog`, which is unacceptable. Finance therefore uses MassTransit's **EF Core Transactional Outbox**: the event row is written to `OutboxMessage` inside the same DB transaction as the business entity; a background delivery service publishes it to RabbitMQ after the commit.

Every consumer is wrapped in `IdempotencyFilter<T>` using Redis `SETNX` keyed by `MessageId` (7-day TTL) so retries and DLQ replays cannot double-post.

**In scope:**
- DI extension `services.AddFinanceMessageBus(configuration, bus => { ... })`
- Per-DbContext outbox configuration via `cfg.AddEntityFrameworkOutbox<TDbContext>(...)` with `UseSqlServer()` + `UseBusOutbox()`
- `MessageRetry` policy: 1 s → 5 s → 15 s, then dead-letter
- `IdempotencyFilter<T>` consume filter (Redis SETNX, 7-day TTL)
- Event-record convention: `sealed record` + `required` properties + `CorrelationId` + `MessageId`
- Domain-event namespace convention: `Finance.ServiceModel/Events/<Domain>/`
- `MassTransitTestHarness` registration in test projects

**Out of scope:**
- Direct (sync) HTTP calls between services — those use Refit + Polly, not the bus
- Saga state machines — defer to a future spec when first multi-step flow appears (payment-allocation cancellation is the likely first candidate)
- Schema registry — JSON contracts via `Finance.ServiceModel` package versioning is sufficient for now

## 2. Behavior

### 2.1 Outbox configuration (MUST)
Every Finance microservice that publishes events MUST:
1. Add `<OutboxMessage>`, `<OutboxState>`, `<InboxState>` EF migrations to its DBModel project.
2. Register `cfg.AddEntityFrameworkOutbox<TDbContext>(o => { o.UseSqlServer(); o.UseBusOutbox(); o.QueryDelay = TimeSpan.FromSeconds(1); o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30); });`
3. Resolve the publishing endpoint as `IPublishEndpoint` (NOT `IBus`) — `AddEntityFrameworkOutbox` rebinds it to write into the outbox table.
4. Publish events inside the same transaction as the entity change. **No try/catch around `Publish` — the outbox guarantees atomicity.**

### 2.2 Event-record convention (MUST)
```csharp
public sealed record JournalEntryPostedEvent
{
    public required Guid CorrelationId { get; init; }
    public required Guid MessageId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required Guid JournalEntryId { get; init; }
    public required string EntryNumber { get; init; }
    public required int FiscalPeriodId { get; init; }
    public required IReadOnlyList<JournalEntryLineDto> Lines { get; init; }
}
```
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

- Startup MUST fail if `RabbitMQ:Host` is missing or unreachable on `/health/ready`.
- Startup MUST fail if `ConnectionStrings:Redis` is missing (idempotency filter depends on it).
- Every event record MUST have `[Required]`-equivalent `required` modifier on `CorrelationId` and `MessageId`.

## 4. Error Rules

| Code | HTTP | Trigger |
|---|---|---|
| `RABBITMQ_UNREACHABLE` | 503 (at /health/ready) | Broker down |
| `DUPLICATE_MESSAGE_SKIPPED` | (logged, not returned) | Idempotency filter caught a replay |
| `MESSAGE_DEAD_LETTERED` | (alert) | Consumer exhausted retries |
| `OUTBOX_GROWTH_ALERT` | (alert) | Outbox row count > 10 000 for > 5 min |

## 5. Versioning Notes

v1: JSON contracts; event-record version implied by package version of `Finance.ServiceModel`. Breaking schema change (renaming a property, removing a required field) MUST publish on a new topic name (e.g., `JournalEntryPostedEvent_v2`) and consumers MUST be upgraded before the old topic is retired.

## 6. Test Plan

| Test | Kind |
|---|---|
| `Publish_WritesToOutbox_AndIsNotDispatched_BeforeCommit` | [Integration] |
| `Publish_DispatchesAfterCommit_WithinQueryDelay` | [Integration] |
| `Consumer_ProcessesEventOnce` | [Integration, Testcontainers RabbitMQ + Redis] |
| `Consumer_SkipsDuplicateMessageId_ViaIdempotencyFilter` | [Integration] |
| `Consumer_RetriesOnTransientException` | [Integration] |
| `Consumer_DeadLettersAfterRetriesExhausted` | [Integration] |
| `EventRecord_FailsToConstruct_WhenCorrelationIdMissing` | [Unit] |
| `EventRecord_FailsToConstruct_WhenMessageIdMissing` | [Unit] |
| `HealthReady_Returns503_WhenRabbitMqDown` | [Integration] |

## 7. Open Items

- Schema registry vs `Finance.ServiceModel` package versioning. v1 sticks with package versioning.
- Cross-vhost federation between Finance and an external customer ERP. Not needed for v1 — they call the Gateway.
