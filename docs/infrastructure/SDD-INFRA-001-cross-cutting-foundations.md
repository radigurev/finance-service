# SDD-INFRA-001 — Cross-Cutting Foundations (Correlation, ProblemDetails, NLog, Versioning)

> Status: Implemented (Batch 2 — the shared `Finance.Infrastructure.Web` library ships the ProblemDetails customization, `IErrorCodeToStatusMap`, `GlobalExceptionHandler`, `ICorrelationIdAccessor` HTTP implementation, and the `AddFinanceServiceDefaults` / `UseFinanceServiceDefaults` host bundle. NLog → Loki, API versioning, and health checks are already wired in `Finance.Accounts.API` + `Finance.Gateway`.)
> Owner: Platform
> Last updated: 2026-05-30
> Related: SDD-INT-AUTH-001, SDD-INFRA-002, SDD-INFRA-009, SDD-OBS-001
> ISA-95: Cross-cutting

---

## 1. Context

Every Finance microservice shares the same baseline of cross-cutting infrastructure: correlation IDs, structured logging, API versioning, ProblemDetails error responses, health checks, and Swagger. These are sourced from the same NuGet packages that Warehouse uses (`Warehouse.Correlation`, `Warehouse.Auth.AspNetCore`, `Warehouse.Common`) so behavior is identical across the two systems.

This spec captures the always-on baseline for the Phase 0 shell. Future specs (`SDD-INFRA-003` sequences, `SDD-INFRA-004` outbox/idempotency, `SDD-OBS-001` distributed tracing, etc.) extend it.

### Resolved Decision (Batch 2) — where the shared web baseline lives
The cross-cutting web baseline ships in a single shared library, `src/Infrastructure/Web/Finance.Infrastructure.Web/` (SDK `Microsoft.NET.Sdk` with `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, referencing `Finance.Common` + `Finance.GenericFiltering`). It hosts:
- `IErrorCodeToStatusMap` (+ `DefaultErrorCodeToStatusMap`) — error-code → HTTP-status mapping (§2.2, §4), DI-registered and overridable.
- `CustomProblemDetailsFactory` + the `InvalidModelStateResponseFactory` that renders FluentValidation `.WithErrorCode(...)` failures as 400 ProblemDetails with codes in the `errors` dictionary and `Title = VALIDATION_FAILED` (§2.2).
- `GlobalExceptionHandler` (`IExceptionHandler`) — maps `FilterValidationException` (from `Finance.GenericFiltering`, SDD-INFRA-005) to a 400 ProblemDetails carrying its `ErrorCode`, and everything else to a 500 ProblemDetails with `Title = CommonErrorCodes.GENERIC_ERROR` (never leaking stack or details). Registered via `AddExceptionHandler` + `UseExceptionHandler`.
- `HttpContextCorrelationIdAccessor` — the `IHttpContextAccessor`-backed implementation of `ICorrelationIdAccessor` (see below).
- `BaseApiController` — `Result` / `Result<T>` → `ActionResult` translation (lives here per SDD-INFRA-009 §2.4).
- Extensions: `AddFinanceProblemDetails()`, `AddFinanceObservability(config)` (SDD-OBS-001 tracing), `AddFinanceServiceDefaults(config, serviceName)`, `EnsureRequiredConfiguration(config, keys[])`, and the pipeline-side `UseFinanceServiceDefaults(app)`. These remove the per-service `Program.cs` drift visible across `Finance.Accounts.API` and `Finance.Gateway` today. Services still register their own `DbContext`, `AddWarehouseAuthentication`, and `AddDbContextCheck<TDbContext>` themselves. **This resolves the SDD-INFRA-001 and SDD-INFRA-009 "where does the web baseline live" open items.**

### Resolved Decision (Batch 2) — `ICorrelationIdAccessor` split
The `ICorrelationIdAccessor` **interface** (`string Get();`) lives in `src/Finance.Common/Abstractions/ICorrelationIdAccessor.cs` — pure, with no ASP.NET dependency — so domain/service assemblies (e.g., `Finance.Infrastructure.Services`, SDD-INFRA-009) can depend on it. The HTTP implementation, `HttpContextCorrelationIdAccessor`, lives in `Finance.Infrastructure.Web` and reads the ambient id from `IHttpContextAccessor` via the `Warehouse.Correlation` middleware item key (`CorrelationIdMiddleware.ItemKey` / `.HeaderName`, the same keys `CorrelationIdRequestTransform` uses in the gateway), falling back to generating one when absent. Both the interface and its implementation are DI-registered by `AddFinanceServiceDefaults`.

## 2. Behavior

### 2.1 Correlation IDs (MUST)
- Every HTTP request MUST be tagged with an `X-Correlation-ID` header (RFC 4122 GUID). If not present on the inbound request, the `CorrelationIdMiddleware` from `Warehouse.Correlation` MUST generate one.
- The correlation ID MUST be returned in the response header and pushed onto the NLog scoped logging context (`scopeproperty:CorrelationId`).
- Outbound HTTP calls (Refit, direct `HttpClient`) MUST include `CorrelationIdDelegatingHandler` so the same ID flows downstream.
- The Finance Gateway (`SDD-INFRA-002`) MUST transform inbound correlation IDs onto the proxied request via `CorrelationIdRequestTransform`.

### 2.2 ProblemDetails responses (MUST)
- All non-2xx responses MUST be RFC 7807 ProblemDetails JSON.
- `title` MUST be the machine-readable error code (SCREAMING_SNAKE_CASE) from a `*ErrorCodes` constant in `Finance.Common.ErrorCodes`.
- `detail` MUST be a short English developer description.
- `type` MUST be `https://finance.local/errors/{code}`.
- Validation failures (400) MUST place codes in the `errors` dictionary (FluentValidation `.WithErrorCode(...)` referencing a constant).
- Frontend i18n key `errors.<CODE>` MUST exist in BOTH `en.ts` and `bg.ts` for every error code introduced.

### 2.3 Structured logging via NLog → Loki (MUST)
- Each service MUST use NLog with `Warehouse.Correlation`-driven scoped properties.
- The NLog target set MUST include console, file, and Loki (Loki endpoint configurable via `LOKI_ENDPOINT` env var).
- The `service` Loki label MUST be a stable kebab-case service name (`finance-accounts-api`, `finance-gateway`).
- No `string` interpolation in log messages — use structured message templates only.

### 2.4 API versioning (MUST)
- All API routes MUST be versioned via URL path: `/api/v{version:apiVersion}/...`.
- Phase 0 ships v1.0 only.
- API versioning is configured via `Asp.Versioning.Mvc` with `AssumeDefaultVersionWhenUnspecified = true`.

### 2.5 Health checks (MUST)
- Every service MUST expose `/health/live` (always 200 if the process is alive) and `/health/ready` (only 200 when all dependencies — DB, message broker, downstream services — are healthy).
- DB-backed services MUST register `AddDbContextCheck<TDbContext>` tagged `ready`.

### 2.6 Swagger (MUST in Development)
- Swagger / OpenAPI MUST be served in the Development environment at `/swagger`.
- Swagger is OFF in non-Development environments by default.

### 2.7 Decimal arithmetic (MUST)
- All monetary values MUST be `decimal` in C# / `DECIMAL(18,2)` in SQL.
- All exchange rates MUST be `decimal` / `DECIMAL(18,6)`.
- `double` and `float` MUST NOT appear on any monetary path. (Enforced by code review; no analyzer yet.)

## 3. Validation

- Configuration validation at startup: `ConnectionStrings:Finance<Service>Db` MUST be non-empty; `Jwt:*` keys MUST be present (per SDD-INT-AUTH-001).
- **Resolved Decision (Batch 2):** the connection-string non-empty check is enforced by the `EnsureRequiredConfiguration(config, keys[])` helper in `Finance.Infrastructure.Web`, which MUST throw a clear, key-naming message if any required key is missing or empty. `Jwt:*`-specific validation is deferred to Batch 7 (SDD-INT-AUTH-001).

## 4. Error Rules

| Code | HTTP | Meaning |
|---|---|---|
| `GENERIC_ERROR` | 500 | Unhandled server error |
| `MISSING_TOKEN` / `INVALID_TOKEN` / `INSUFFICIENT_PERMISSIONS` | 401 / 401 / 403 | See SDD-INT-AUTH-001 |

Domain-specific error codes live in domain SDDs (e.g., `SDD-ACCT-001` lists `INVALID_ACCOUNT_CODE`, …).

## 5. Versioning

This is a meta-spec describing the baseline shape every Finance microservice follows. New baseline rules are added by amending this spec via a `CHG-ENH-*` change spec.

## 6. Test Plan

| Test name | Kind |
|---|---|
| `CorrelationIdMiddleware_GeneratesIdWhenMissing` | [Integration] |
| `CorrelationIdMiddleware_PropagatesInboundId` | [Integration] |
| `CorrelationIdMiddleware_SetsResponseHeader` | [Integration] |
| `ProblemDetails_ReturnsErrorCodeAsTitle_OnValidationFailure` | [Integration] |
| `HealthChecks_LiveReturns200Always` | [Integration] |
| `HealthChecks_ReadyReturns503_WhenDatabaseDown` | [Integration] |
| `ApiVersioning_RoutesV1Correctly` | [Integration] |

## 7. Resolved Decisions & Open Items

### Resolved (Batch 2)
- **Web baseline location:** the shared `Finance.Infrastructure.Web` library hosts the ProblemDetails customization, `IErrorCodeToStatusMap`, `GlobalExceptionHandler`, `BaseApiController`, `HttpContextCorrelationIdAccessor`, and the `AddFinanceServiceDefaults` / `UseFinanceServiceDefaults` host bundle (see §1).
- **`ICorrelationIdAccessor` split:** interface in `Finance.Common/Abstractions/`, HTTP impl in `Finance.Infrastructure.Web` (see §1).
- **Distributed tracing (OpenTelemetry → Jaeger):** `AddFinanceObservability(config)` ships now via `Finance.Infrastructure.Web` (SDD-OBS-001 Batch-2 scope: tracing MUST). Prometheus `/metrics` + Grafana dashboards remain a Phase-7 SHOULD and are deferred (SDD-OBS-001 §2.5–2.6).

### Open
- Redis cache (`AddWarehouseRedisCache`) is wired in Phase 1 alongside the first cache-eligible reference dataset (currencies).
- MassTransit + transactional outbox is wired in Phase 3 when the first event publisher (Journal) ships.
