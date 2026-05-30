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
| `SDD-INFRA-003` Centralized Sequence Generation (Auto-Code, gapless per НАП) | Draft | — | (planned: `Finance.Infrastructure.Sequences`) | — |
| `SDD-INFRA-004` Redis Distributed Cache | Draft | — | (planned: `Finance.Infrastructure.Caching`) | — |
| `SDD-INFRA-005` Generic Filtering (IQueryable, filter/sort/page) | Draft -> in progress | `src/Finance.GenericFiltering.Tests` (NUnit; `[Category("Integration")]` SQL test excluded) | `src/Finance.GenericFiltering/Finance.GenericFiltering.csproj` (`FilterRequest`, `FilterCriterion`, `SortCriterion`, `PagedResult<T>`, `[Filterable]`/`[Sortable]`/`[Searchable]`, `ApplyFilter`, `FilterValidationException`); `src/Finance.Common/ErrorCodes/FilterErrorCodes.cs` | (planned: shared filter UI components) |
| `SDD-INFRA-006` Resilient Message Publisher (MassTransit + Outbox + Idempotency) | Draft | — | (planned: `Finance.Infrastructure.Messaging`) | — |
| `SDD-INFRA-007` Validation Chain | Draft -> in progress | `src/Finance.Common.Tests` (NUnit; `[Category("Integration")]` validator test excluded) | `src/Finance.Common/Validation/` (`IChainValidator<TRequest>`, `ChainValidationResult`, `ValidationChain<TRequest>`, `AddValidationChain<TRequest>()`); generic codes in `src/Finance.Common/ErrorCodes/CommonErrorCodes.cs` | — |
| `SDD-INFRA-008` Workflow Engine (state machine) | Active | Batch 1: `src/Finance.Common.Tests` (interface/context tests). Batch 2: `src/Infrastructure/Finance.Infrastructure.Tests` (`WorkflowEngine` legal/illegal transition, `WORKFLOW_GUARD_FAILED`, `STATE_NOT_REGISTERED` — all `[Unit]`; caller-side DB tests `[Category("Integration")]` excluded) | Batch 1: `src/Finance.Common/Workflow/` (`IWorkflowState<TAggregate>`, `IWorkflowEngine<TAggregate>`, `WorkflowContext<TAggregate>`); `src/Finance.Common/ErrorCodes/WorkflowErrorCodes.cs`. Batch 2: `src/Infrastructure/Services/Finance.Infrastructure.Services/` (`WorkflowEngine<TAggregate>` + `AddWorkflowEngine<TAggregate>()`, per-aggregate keyed state registry; v1 engine validates + runs hooks, caller owns SaveChanges/RowVersion/status-history) | (planned: aggregate detail page `availableTransitions` array) |
| `SDD-INFRA-009` Base Entity Service & Common Service Helpers | Active | Batch 1: `src/Finance.Common.Tests` (`Result`/`Result<T>` tests). Batch 2: `src/Infrastructure/Finance.Infrastructure.Tests` (`BaseEntityService` find present/missing + `SearchableServiceBase` filter/page/project/override via SQLite in-memory; `PrimaryFlagHelper` 3 cases; `[Category("Integration")]` real-SQL tests excluded) | Batch 1: `src/Finance.Common/Results/` (`Result`, `Result<T>`). Batch 2: `src/Infrastructure/Services/Finance.Infrastructure.Services/` (`BaseEntityService<TContext>`, `SearchableServiceBase<TEntity, TDto, TContext>`, `PrimaryFlagHelper`); `BaseApiController` + `IErrorCodeToStatusMap` live in `src/Infrastructure/Web/Finance.Infrastructure.Web/` (see SDD-INFRA-001) | — |
| `SDD-OBS-001` Observability (NLog → Loki, OpenTelemetry → Jaeger, Prometheus + Grafana) | Active (tracing) | `src/Infrastructure/Finance.Infrastructure.Tests` (`Observability_StampsCorrelationIdAsActivityTag` `[Unit]`; traceparent/OTLP integration tests deferred) | Batch 2 tracing: `AddFinanceObservability(config)` in `src/Infrastructure/Web/Finance.Infrastructure.Web/` (ASP.NET Core + HttpClient + EF Core instrumentation, OTLP exporter, `correlation_id` Activity tag). Already wired: NLog → Loki in `Finance.Accounts.API` + `Finance.Gateway` nlog.config. Deferred (Phase 7): Prometheus `/metrics` + Grafana dashboards | — |
| `SDD-AUDIT-001` Immutable Audit Trail | Draft | — | (planned: `Finance.Infrastructure.Audit`, `audit.OperationsEvents` table per service) | (planned: audit log panel on every aggregate detail page) |
| `SDD-EVTLOG-001` Centralized Event Log Service | Draft | — | (planned: `Finance.EventLog.API` port 6008) | — |

## UI

| Spec | Status | Tests | Implementation | Frontend |
|---|---|---|---|---|
| `SDD-UI-001` Frontend Shell (React + MUI + i18n + Density) | Draft (shell) | (planned) | — | `frontend/src/main.tsx`, `app/{App,AppShell}.tsx`, `shared/stores/{auth,layout,theme}.ts`, `shared/i18n/locales/{en,bg}.ts`, `shared/api/axios.ts`, `shared/utils/getApiErrorMessage.ts`, `features/auth/{LoginPage,RequireAuth}.tsx`, `features/accounts/AccountsListPage.tsx` |
| `SDD-UI-002` Modal vs Page Form Mode + `useGoBack` | Planned | — | — | — |
