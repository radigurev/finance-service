# SDD-INT-AUTH-001 — Shared JWT Authentication with auth-service

> Status: Draft
> Owner: Platform
> Related: SDD-INFRA-001, SDD-INFRA-002, SDD-ACCT-001
> ISA-95: Level 4 cross-cutting (Personnel Model — federated)

---

## 1. Context

Finance does NOT operate its own identity store. It validates JWTs issued by the standalone `auth-service` (a separate repository) using the shared `Warehouse.Auth.AspNetCore` NuGet package published to GitHub Packages. The same user accounts and bearer tokens that authenticate against Warehouse also authenticate against Finance.

The auth-service exposes a permission catalogue and per-user permissions API; Finance services validate the user's permissions for each request by consulting that API (with Redis caching once the cache is wired in).

## 2. Behavior

### 2.1 JWT validation (MUST)
- Every Finance microservice MUST call `services.AddWarehouseAuthentication(configuration)` from `Warehouse.Auth.AspNetCore` during startup.
- The `Jwt:SecretKey`, `Jwt:Issuer`, and `Jwt:Audience` configuration values MUST match the values used by `auth-service` to issue tokens. Mismatch results in 401 responses for valid Warehouse tokens.
- The middleware MUST validate issuer, audience, signing key, and lifetime; `ClockSkew` MUST be `Zero`.

### 2.2 Permission-based authorization (MUST)
- Every endpoint MUST be decorated with `[RequirePermission("finance.<resource>:<action>")]` from `Warehouse.Auth.AspNetCore`.
- Resource names MUST be lowercase, dot-separated; actions MUST be one of `read`, `write`, `delete`, `post`, `approve`.
- Permissions MUST be registered with auth-service on first run (see §2.4).
- For the shell milestone (Phase 0), permissions `finance.account:read` and `finance.account:write` are introduced.

### 2.3 Bearer token forwarding for downstream calls (MUST)
- When a Finance service calls auth-service for permission lookups, it MUST forward the inbound bearer token via `BearerTokenForwardingHandler` (registered automatically by `AddWarehousePermissionValidation`).
- This preserves the user identity across the call chain so auth-service can return the correct permission set.

### 2.4 Permission registration (MUST — Phase 1)
- Each Finance service MUST register its required permissions with auth-service on application startup via the auth-service permission-registration endpoint.
- Permission descriptors MUST include: `code` (`finance.<resource>:<action>`), `domain` (`finance`), and `description` (human-readable).
- Registration MUST be idempotent — repeated startups MUST NOT create duplicates.
- For the shell (Phase 0), permission registration is deferred. Permissions are seeded manually in auth-service.

### 2.5 Token issuance (out of scope)
- Finance MUST NOT issue tokens. All login, refresh, and password operations go to auth-service through the Finance gateway.

## 3. Validation

- Configuration validation at startup: `Jwt:SecretKey` MUST be at least 32 characters; `Jwt:Issuer` and `Jwt:Audience` MUST be non-empty. Missing values cause startup to fail with a clear error.

## 4. Error Rules

| Code | HTTP | Meaning |
|---|---|---|
| `MISSING_TOKEN` | 401 | No bearer token in `Authorization` header |
| `INVALID_TOKEN` | 401 | Signature/issuer/audience/lifetime check failed |
| `INSUFFICIENT_PERMISSIONS` | 403 | Authenticated but lacks required `finance.<resource>:<action>` |

All emitted as ProblemDetails with `title` = the code and `type` = `https://finance.local/errors/{code}`.

## 5. Versioning

This contract is versioned together with the `Warehouse.Auth.AspNetCore` NuGet package. Breaking changes to JWT claims or the permission attribute trigger a major version bump in that package; Finance services pin to compatible major versions via `0.1.*` floating ranges until the package reaches 1.0.

## 6. Test Plan

| Test name | Kind |
|---|---|
| `AddWarehouseAuthentication_RejectsInvalidIssuer` | [Integration] |
| `RequirePermission_ReturnsForbidden_WhenPermissionMissing` | [Integration] |
| `RequirePermission_AllowsRequest_WhenPermissionPresent` | [Integration] |
| `BearerTokenForwarding_PreservesTokenAcrossDownstreamCalls` | [Integration] |
| `StartupFails_WhenJwtSecretKeyTooShort` | [Unit] |

## 7. Open Items

- Service-to-service (S2S) JWT issuance for Finance → Warehouse calls. Current placeholder: a static `service:finance-integration` permission and a long-lived token minted out-of-band. Final design tracked in `CHG-FEAT-001`.
- JWKS-based key rotation. Today we ship a symmetric `Jwt:SecretKey`; rotation requires coordinated updates across all services. Asymmetric (RS256) is on the roadmap.
