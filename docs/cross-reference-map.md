# Cross-Reference Map

Every SDD must map to its tests, implementation files, and (if applicable) frontend features. Update this file in the same PR as the spec.

Statuses:
- **Planned** — not yet drafted
- **Draft** — spec written; implementation pending
- **Draft (shell)** — spec written; minimal scaffolding in place
- **Draft -> in progress** — spec resolved & authoritative; implementation actively underway (e.g., Batch 1 shipping)
- **Active** — spec resolved & authoritative; the code it describes ships in the named batch (open items, if any, are scoped/deferred)
- **Implemented** — fully implemented and tested

## Core engine

| Spec | Status | Tests | Implementation | Frontend |
|---|---|---|---|---|
| `SDD-FIN-001` Double-Entry Engine | Planned | — | — | — |
| `SDD-FIN-002` Journal Entry Lifecycle | Planned | — | — | — |
| `SDD-FIN-003` General Ledger & Trial Balance | Planned | — | — | — |
| `SDD-FIN-004` Fiscal Period Management | Planned | — | — | — |
| `SDD-FIN-005` Multi-Currency Engine | Planned | — | — | — |
| `SDD-FIN-006` Posting Engine + Posting Rules | Planned | — | — | — |

## Domain

| Spec | Status | Tests | Implementation | Frontend |
|---|---|---|---|---|
| `SDD-ACCT-001` Chart of Accounts | Draft (shell) | (planned) | `src/Interfaces/Accounts/Finance.Accounts.API/Controllers/AccountsController.cs`, `Services/AccountService.cs`, `AccountRepository.cs`, `Validators/CreateAccountRequestValidator.cs`; `Databases/Finance.Accounts.DBModel/Models/Account.cs`, `Configurations/AccountConfiguration.cs` | `frontend/src/features/accounts/AccountsListPage.tsx` |
| `SDD-NOM-001` Nomenclature Reference Data (currencies + Warehouse country/state/city proxy) | Draft | — | (planned: `Finance.Nomenclature.API`) | (planned: shared `useNomenclature()` hook) |
| `SDD-INV-001` Invoice Lifecycle | Planned | — | — | — |
| `SDD-PAY-001` Payment Recording & Matching | Planned | — | — | — |
| `SDD-PAY-002` Settlement & Allocation | Planned | — | — | — |
| `SDD-RPT-001` Trial Balance | Planned | — | — | — |
| `SDD-RPT-002` Balance Sheet + Income Statement | Planned | — | — | — |
| `SDD-RPT-003` VAT Journals | Planned | — | — | — |
| `SDD-CTRY-001` Country Strategy Interface | Planned | — | — | — |
| `SDD-CTRY-BG-001` Bulgaria Strategy | Planned | — | — | — |

## Integration

| Spec | Status | Tests | Implementation | Frontend |
|---|---|---|---|---|
| `SDD-INT-AUTH-001` Shared JWT Authentication | Draft (shell) | (planned) | `src/Interfaces/Accounts/Finance.Accounts.API/Program.cs` (AddWarehouseAuthentication); `[RequirePermission(...)]` on `AccountsController` | `frontend/src/features/auth/LoginPage.tsx`, `RequireAuth.tsx`, `shared/api/axios.ts`, `shared/stores/auth.ts` |
| `SDD-INT-WH-001` Warehouse Event Subscriptions | Planned | — | — | — |
| `SDD-INT-WH-002` Finance → Warehouse Refit Client | Planned | — | — | — |
| `SDD-INT-BNB-001` BNB Exchange-Rate Provider | Planned | — | — | — |
| `SDD-INT-NAP-001` НАП Regulatory Export | Planned | — | — | — |

## Infrastructure (cross-cutting)

| Spec | Status | Tests | Implementation | Frontend |
|---|---|---|---|---|
| `SDD-INFRA-001` Cross-Cutting Foundations (correlation, ProblemDetails, NLog, versioning, health, decimal arithmetic) | Active | `src/Infrastructure/Finance.Infrastructure.Tests` (Batch 2: `DefaultErrorCodeToStatusMap`, `ToActionResult`/ProblemDetails, `correlation_id` Activity tag; `[Category("Integration")]` excluded) | Batch 2 shared web library `src/Infrastructure/Web/Finance.Infrastructure.Web/` (`IErrorCodeToStatusMap`/`DefaultErrorCodeToStatusMap`, `CustomProblemDetailsFactory` + `InvalidModelStateResponseFactory`, `GlobalExceptionHandler`, `HttpContextCorrelationIdAccessor`, `BaseApiController`, `AddFinanceProblemDetails`/`AddFinanceObservability`/`AddFinanceServiceDefaults`/`UseFinanceServiceDefaults`/`EnsureRequiredConfiguration`); `ICorrelationIdAccessor` interface in `src/Finance.Common/Abstractions/ICorrelationIdAccessor.cs`. Already wired: `src/Interfaces/Accounts/Finance.Accounts.API/Program.cs`, `nlog.config`, `appsettings.json.template`; `src/Finance.Common/ErrorCodes/` | `frontend/src/shared/api/axios.ts`, `shared/utils/getApiErrorMessage.ts` |
| `SDD-INFRA-002` Finance Gateway (YARP) | Draft (shell) | (planned) | `src/Infrastructure/Gateway/Finance.Gateway/Program.cs`, `CorrelationIdRequestTransform.cs`, `appsettings.json.template` | — |
| `SDD-INFRA-003` Centralized Sequence Generation (Auto-Code, gapless per НАП) | Active (library write-path; per-service table/migration + `ICountryStrategy` integration deferred) | Batch 3: `src/Infrastructure/Finance.Infrastructure.Tests` (`NextAsync` SQLite in-memory unit tests, `DefaultDocumentNumberFormatter`, built-in-key uniqueness; real-SQL `UPDLOCK, HOLDLOCK` concurrency test `[Category("Integration")]` excluded) | Batch 3: `src/Infrastructure/Sequences/Finance.Infrastructure.Sequences/` (`ISequenceGenerator`/`SequenceGenerator`, `SequenceCounter` entity + config schema `infrastructure`/table `Sequences`, `IDocumentNumberFormatter` + `DefaultDocumentNumberFormatter` seam, 7 built-in keys JE/PINV/SINV/CN/DN/PAY/RCT, `AddSequenceGenerator<TDbContext>()`); codes in `src/Finance.Common/ErrorCodes/SequenceErrorCodes.cs`. Deferred: `infrastructure.Sequences` table/migration per service DbContext (Batch 4+); `ICountryStrategy.GenerateDocumentNumber` (SDD-CTRY-001) | — |
| `SDD-INFRA-004` Redis Distributed Cache | Active (library cache-aside v1; cross-service pub/sub invalidation deferred to Phase 5) | Batch 3: `src/Infrastructure/Finance.Infrastructure.Tests` (key-prefix validation, TTL bounds, unbounded-pattern rejection, fall-through-when-Redis-down, startup config — all `[Unit]`; real-Redis `SCAN`/round-trip tests `[Category("Integration")]` excluded) | Batch 3: `src/Infrastructure/Caching/Finance.Infrastructure.Caching/` (`ICacheService<T>` `GetOrSetAsync`/`RemoveAsync`/`RemoveByPatternAsync`, `RedisCacheService` over StackExchange.Redis `IConnectionMultiplexer` (SCAN, `{service}:` bounded), System.Text.Json v1, `AddFinanceRedisCache(config)` — OWNS the lazy `IConnectionMultiplexer` registration reused by Messaging); codes in `src/Finance.Common/ErrorCodes/CachingErrorCodes.cs`. Deferred: cross-service `<Entity>InvalidatedEvent` pub/sub (Phase 5) | — |
| `SDD-INFRA-005` Generic Filtering (IQueryable, filter/sort/page) | Draft -> in progress | `src/Finance.GenericFiltering.Tests` (NUnit; `[Category("Integration")]` SQL test excluded) | `src/Finance.GenericFiltering/Finance.GenericFiltering.csproj` (`FilterRequest`, `FilterCriterion`, `SortCriterion`, `PagedResult<T>`, `[Filterable]`/`[Sortable]`/`[Searchable]`, `ApplyFilter`, `FilterValidationException`); `src/Finance.Common/ErrorCodes/FilterErrorCodes.cs` | (planned: shared filter UI components) |
| `SDD-INFRA-006` Resilient Message Publisher (MassTransit + Outbox + Idempotency) | Active (library wiring + `IdempotencyFilter` + `IFinanceEvent` marker; per-service outbox tables + concrete events deferred) | Batch 3: `src/Infrastructure/Finance.Infrastructure.Tests` (`IFinanceEvent` shape, `IdempotencyFilter` skip/process via `MassTransitTestHarness`, startup config fail-fast — all `[Unit]`; real RabbitMQ/Redis/SQL-outbox tests `[Category("Integration")]` excluded) | Batch 3: `src/Infrastructure/Messaging/Finance.Infrastructure.Messaging/` (references `Finance.Infrastructure.Caching`; `AddFinanceMessageBus<TDbContext>(config)` with `AddEntityFrameworkOutbox<TDbContext>` UseSqlServer+UseBusOutbox/QueryDelay 1s/DuplicateDetectionWindow 30m + RabbitMQ host + MessageRetry 1s/5s/15s→`<queue>_error`, `IdempotencyFilter<T>` over Caching's Redis multiplexer via `UseFinanceIdempotency()`, `MassTransitTestHarness` helper). Marker `IFinanceEvent` in `src/Finance.ServiceModel/Events/` (only ServiceModel edit this batch). Deferred: `OutboxMessage`/`OutboxState`/`InboxState` tables+migrations per publishing service DbContext (Batch 4+); concrete domain events (later batches) | — |
| `SDD-INFRA-007` Validation Chain | Draft -> in progress | `src/Finance.Common.Tests` (NUnit; `[Category("Integration")]` validator test excluded) | `src/Finance.Common/Validation/` (`IChainValidator<TRequest>`, `ChainValidationResult`, `ValidationChain<TRequest>`, `AddValidationChain<TRequest>()`); generic codes in `src/Finance.Common/ErrorCodes/CommonErrorCodes.cs` | — |
| `SDD-INFRA-008` Workflow Engine (state machine) | Active | Batch 1: `src/Finance.Common.Tests` (interface/context tests). Batch 2: `src/Infrastructure/Finance.Infrastructure.Tests` (`WorkflowEngine` legal/illegal transition, `WORKFLOW_GUARD_FAILED`, `STATE_NOT_REGISTERED` — all `[Unit]`; caller-side DB tests `[Category("Integration")]` excluded) | Batch 1: `src/Finance.Common/Workflow/` (`IWorkflowState<TAggregate>`, `IWorkflowEngine<TAggregate>`, `WorkflowContext<TAggregate>`); `src/Finance.Common/ErrorCodes/WorkflowErrorCodes.cs`. Batch 2: `src/Infrastructure/Services/Finance.Infrastructure.Services/` (`WorkflowEngine<TAggregate>` + `AddWorkflowEngine<TAggregate>()`, per-aggregate keyed state registry; v1 engine validates + runs hooks, caller owns SaveChanges/RowVersion/status-history) | (planned: aggregate detail page `availableTransitions` array) |
| `SDD-INFRA-009` Base Entity Service & Common Service Helpers | Active | Batch 1: `src/Finance.Common.Tests` (`Result`/`Result<T>` tests). Batch 2: `src/Infrastructure/Finance.Infrastructure.Tests` (`BaseEntityService` find present/missing + `SearchableServiceBase` filter/page/project/override via SQLite in-memory; `PrimaryFlagHelper` 3 cases; `[Category("Integration")]` real-SQL tests excluded) | Batch 1: `src/Finance.Common/Results/` (`Result`, `Result<T>`). Batch 2: `src/Infrastructure/Services/Finance.Infrastructure.Services/` (`BaseEntityService<TContext>`, `SearchableServiceBase<TEntity, TDto, TContext>`, `PrimaryFlagHelper`); `BaseApiController` + `IErrorCodeToStatusMap` live in `src/Infrastructure/Web/Finance.Infrastructure.Web/` (see SDD-INFRA-001) | — |
| `SDD-OBS-001` Observability (NLog → Loki, OpenTelemetry → Jaeger, Prometheus + Grafana) | Active (tracing) | `src/Infrastructure/Finance.Infrastructure.Tests` (`Observability_StampsCorrelationIdAsActivityTag` `[Unit]`; traceparent/OTLP integration tests deferred) | Batch 2 tracing: `AddFinanceObservability(config)` in `src/Infrastructure/Web/Finance.Infrastructure.Web/` (ASP.NET Core + HttpClient + EF Core instrumentation, OTLP exporter, `correlation_id` Activity tag). Already wired: NLog → Loki in `Finance.Accounts.API` + `Finance.Gateway` nlog.config. Deferred (Phase 7): Prometheus `/metrics` + Grafana dashboards | — |
| `SDD-AUDIT-001` Immutable Audit Trail | Active (library write-path; export endpoint + frontend + Parquet archival + DB INSERT-only grants + nightly verification deferred) | Batch 3: `src/Infrastructure/Finance.Infrastructure.Tests` (`RecordAsync` persists into in-memory `IAuditDbContext`, no own SaveChanges, `AUDIT_REASON_REQUIRED` on sensitive op, BeforeJson null-on-create/required-on-update, `AuditEntry` required-field construction — all `[Unit]` via SQLite in-memory; real-SQL same-transaction/audit-before-outbox/DENY-grant tests `[Category("Integration")]` excluded) | Batch 3: `src/Infrastructure/Audit/Finance.Infrastructure.Audit/` (`IAuditService.RecordAsync(AuditEntry, ct)`, `AuditService<TContext>` where `TContext:DbContext,IAuditDbContext`, `IAuditDbContext { DbSet<OperationsEvent> }`, `OperationsEvent` entity + config schema `audit`, `AuditEntry` sealed record, `AddFinanceAudit<TContext>()`; audit-first-before-outbox ordering, no own SaveChanges); codes in `src/Finance.Common/ErrorCodes/AuditErrorCodes.cs`. Deferred: `audit.OperationsEvents` INSERT-only grant + migration per service (Batch 4+), `GET /api/v1/audit/export`, Parquet archival, nightly tamper job | (deferred: audit log panel on every aggregate detail page) |
| `SDD-EVTLOG-001` Centralized Event Log Service | Draft | — | (planned: `Finance.EventLog.API` port 6008) | — |

## UI

| Spec | Status | Tests | Implementation | Frontend |
|---|---|---|---|---|
| `SDD-UI-001` Frontend Shell (React + MUI + i18n + Density) | Draft (shell) | (planned) | — | `frontend/src/main.tsx`, `app/{App,AppShell}.tsx`, `shared/stores/{auth,layout,theme}.ts`, `shared/i18n/locales/{en,bg}.ts`, `shared/api/axios.ts`, `shared/utils/getApiErrorMessage.ts`, `features/auth/{LoginPage,RequireAuth}.tsx`, `features/accounts/AccountsListPage.tsx` |
| `SDD-UI-002` Modal vs Page Form Mode + `useGoBack` | Planned | — | — | — |
