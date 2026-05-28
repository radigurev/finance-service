# SDD-INFRA-001 — Cross-Cutting Foundations (Correlation, ProblemDetails, NLog, Versioning)

> Status: Draft
> Owner: Platform
> Related: SDD-INT-AUTH-001, SDD-INFRA-002, SDD-OBS-001 (future)
> ISA-95: Cross-cutting

---

## 1. Context

Every Finance microservice shares the same baseline of cross-cutting infrastructure: correlation IDs, structured logging, API versioning, ProblemDetails error responses, health checks, and Swagger. These are sourced from the same NuGet packages that Warehouse uses (`Warehouse.Correlation`, `Warehouse.Auth.AspNetCore`, `Warehouse.Common`) so behavior is identical across the two systems.

This spec captures the always-on baseline for the Phase 0 shell. Future specs (`SDD-INFRA-003` sequences, `SDD-INFRA-004` outbox/idempotency, `SDD-OBS-001` distributed tracing, etc.) extend it.

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

## 7. Open Items

- Distributed tracing (OpenTelemetry → Jaeger) is in `SDD-OBS-001` (deferred to Phase 1).
- Redis cache (`AddWarehouseRedisCache`) is wired in Phase 1 alongside the first cache-eligible reference dataset (currencies).
- MassTransit + transactional outbox is wired in Phase 3 when the first event publisher (Journal) ships.
