# SDD-INFRA-002 — Finance Gateway (YARP)

> Status: Draft
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
- Failing downstream services MUST cause the gateway's `/health` to return 503.

### 2.5 No business logic (MUST)
- The gateway MUST NOT execute any business code. It MUST NOT authenticate (it forwards the `Authorization` header untouched); it MUST NOT decode JWT claims; it MUST NOT log payloads.

## 3. Validation

- `HealthChecks:*` config values MUST be valid URIs at startup.
- `ReverseProxy:Clusters` MUST have at least one destination per cluster.

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

| Test name | Kind |
|---|---|
| `Gateway_ProxiesAccountsRouteToAccountsApi` | [Integration] |
| `Gateway_ProxiesAuthRouteToAuthService` | [Integration] |
| `Gateway_AddsCorrelationIdHeaderToOutboundProxyRequest` | [Integration] |
| `Gateway_ReturnsRateLimited_WhenIpExceedsGlobalLimit` | [Integration] |
| `Gateway_HealthEndpoint_Returns503_WhenDownstreamReadyFails` | [Integration] |

## 7. Open Items

- Service-to-service JWT minting / verification for Warehouse → Finance calls. Today the gateway just forwards bearer tokens; once `SDD-INT-AUTH-001 §Open Items` is resolved, a `ServiceToServiceJwtHandler` will be added for inbound S2S authentication.
- Per-route auth pre-check at the gateway (e.g., reject obviously unauthenticated requests early). Today auth is enforced only at the downstream service.
