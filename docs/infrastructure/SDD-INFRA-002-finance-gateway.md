# SDD-INFRA-002 — Finance Gateway (YARP)

> Status: Implemented (Batch 7 — config-driven YARP gateway shipped; startup validation + dynamic per-cluster health aggregation added; business-logic-free)
> Owner: Platform
> Related: SDD-INT-AUTH-001, SDD-INFRA-001
> ISA-95: Cross-cutting

---

## 1. Context

`Finance.Gateway` is the single external entrypoint to all Finance services. It is also the surface that the Warehouse system calls into when it needs to read Finance data (e.g., to display invoice/payment status on a Sales Order detail page). Internal microservices are NOT exposed directly — they are only reachable via the gateway on the shared `platform_net` Docker network.

The gateway uses YARP (Yet Another Reverse Proxy) with config-driven routing.

## 2. Behavior

### 2.1 Routing (MUST)
- Routes MUST be config-driven via the `ReverseProxy` section of `appsettings.json`.
- Each backend service MUST have its own YARP cluster (`accounts-cluster`, `journal-cluster`, …).
- Auth-related paths (`/api/v1/auth/**`, `/api/v1/users/**`, `/api/v1/roles/**`, `/api/v1/permissions/**`) MUST be proxied to the standalone auth-service via the `auth-cluster`.
- A 404 on an unmatched path is the gateway's default behavior.

### 2.2 Correlation ID (MUST)
- The gateway MUST run `CorrelationIdMiddleware` BEFORE `MapReverseProxy()`.
- The gateway MUST install the `CorrelationIdRequestTransform` so the inbound `X-Correlation-ID` (generated or forwarded) is copied onto the outgoing proxy request headers.

### 2.3 Rate limiting (MUST)
- A global per-IP limit MUST be enforced: 200 requests per minute, queue limit 20, oldest-first.
- A named `fixed` policy MUST be available for opting individual routes into a tighter 100/min limit.
- Rate-limit rejections MUST return HTTP 429.

### 2.4 Health aggregation (MUST)
- The gateway MUST expose `/health` that aggregates `/health/ready` from each downstream cluster's primary destination.
- The readiness checks MUST cover ALL configured clusters (`auth`, `accounts`, `nomenclature`, `eventlog`, and any future cluster). The check set MUST be DERIVED from the `ReverseProxy:Clusters` configuration — for each cluster the gateway takes that cluster's first destination address and appends `/health/ready` — so that adding a new cluster automatically extends health aggregation without code changes. The gateway MUST NOT rely on a hard-coded per-service `AddUrlGroup` list.
- Each derived readiness check MUST be tagged `ready` so it participates in the `/health` aggregation.
- Failing downstream services MUST cause the gateway's `/health` to return 503; when all derived readiness checks pass, `/health` MUST return 200.

### 2.5 No business logic (MUST)
- The gateway MUST NOT execute any business code. It MUST NOT authenticate (it forwards the `Authorization` header untouched); it MUST NOT decode JWT claims; it MUST NOT validate JWT configuration (JWT validation is owned by each downstream service per SDD-INT-AUTH-001); it MUST NOT log payloads.

### 2.6 Testability (MUST)
- The gateway entrypoint MUST expose `public partial class Program { }` so a `WebApplicationFactory<Program>` can host it in-process for tests.
- The `ReverseProxy` and `HealthChecks` configuration sections MUST be overridable in tests (e.g., via in-memory configuration / `WebApplicationFactory` configuration overrides), so proxy, rate-limit, correlation, and health behavior can be exercised against in-process WireMock.Net stand-ins without real downstream services.

## 3. Validation

### 3.1 Startup validation — fail fast (MUST)
- At startup, BEFORE `app.Run()`, the gateway MUST validate its routing and health configuration and MUST fail fast with a clear, actionable message when validation fails.
- Every `HealthChecks:*` configuration value (and every cluster destination address derived for health aggregation) MUST be a valid ABSOLUTE URI. A relative, malformed, or empty URI MUST abort startup with a message naming the offending key/cluster.
- Every cluster under `ReverseProxy:Clusters` MUST declare at least one destination. A cluster with zero destinations MUST abort startup with a message naming the offending cluster.
- Validation MUST run before the application begins serving traffic; a misconfigured gateway MUST NOT start in a partially-working state.

## 4. Error Rules

| Code | HTTP | Meaning |
|---|---|---|
| (YARP standard) | 502 | Bad gateway — downstream service did not return a response |
| (YARP standard) | 504 | Gateway timeout |
| (rate limiter) | 429 | Per-IP or named policy limit exceeded |

Gateway-level errors are not wrapped in ProblemDetails (YARP returns the downstream response verbatim).

## 5. Versioning

- The gateway's external surface is `/api/v1/*`. Adding new clusters or routes is additive and does NOT require a version bump.
- Path-shape changes to existing routes MUST go through a `CHG-ENH-*` change spec.

## 6. Test Plan

All gateway tests run in-process via `WebApplicationFactory<Program>` with downstream services replaced by in-process WireMock.Net stubs. Because WireMock.Net needs NO Docker, these tests are RUNNABLE in the default suite and MUST NOT be marked `[Category("Integration")]`. They live in `src/Infrastructure/Gateway/Finance.Gateway.Tests` and carry `[Category("SDD-INFRA-002")]`.

| Test name | Kind |
|---|---|
| `Gateway_ProxiesAccountsRouteToAccountsApi` | [Integration] |
| `Gateway_ProxiesAuthRouteToAuthService` | [Integration] |
| `Gateway_AddsCorrelationIdHeaderToOutboundProxyRequest` | [Integration] |
| `Gateway_ReturnsRateLimited_WhenIpExceedsGlobalLimit` | [Integration] |
| `Gateway_HealthEndpoint_Returns200_WhenAllDerivedClusterReadyChecksPass` | [Integration] |
| `Gateway_HealthEndpoint_Returns503_WhenDownstreamReadyFails` | [Integration] |
| `Gateway_HealthAggregation_DerivesReadyCheckPerConfiguredCluster` | [Unit] |
| `Gateway_Startup_Fails_WhenClusterHasNoDestination` | [Unit] |
| `Gateway_Startup_Fails_WhenHealthCheckUriNotAbsolute` | [Unit] |

> The `[Integration]`-kind rows above are in-process WireMock.Net + `WebApplicationFactory<Program>` tests; per project policy they DO run in the default suite (they require no real external infrastructure). Only tests requiring REAL external infrastructure (real auth-service permission lookup, real SQL/Redis/RabbitMQ) carry the excluded `[Category("Integration")]` marker.

## 7. Open Items

- Service-to-service JWT minting / verification for Warehouse → Finance calls. Today the gateway just forwards bearer tokens; once `SDD-INT-AUTH-001 §Open Items` is resolved, a `ServiceToServiceJwtHandler` will be added for inbound S2S authentication.
- Per-route auth pre-check at the gateway (e.g., reject obviously unauthenticated requests early). Today auth is enforced only at the downstream service.

## 8. Versioning Notes

- **v1 — Initial specification (Draft, shell).** Config-driven YARP routing, correlation-ID transform, rate limiting, hard-coded auth+accounts health aggregation.
- **v2 — Batch 7 (Active, non-breaking).** Recorded the shipped gateway and added two behaviors: (a) fail-fast startup validation of `HealthChecks:*` absolute URIs and ≥1 destination per `ReverseProxy:Clusters` cluster before `app.Run()`; (b) dynamic per-cluster health aggregation DERIVED from `ReverseProxy:Clusters` (replacing the hard-coded `AddUrlGroup` list) so new clusters are covered automatically. Reaffirmed the gateway is business-logic-free (no JWT decode/validation) and added the testability contract (`public partial class Program { }`, overridable config). Non-breaking: the external `/api/v1/*` surface is unchanged.
