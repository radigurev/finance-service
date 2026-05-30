# SDD-INT-AUTH-001 — Shared JWT Authentication with auth-service

> Status: Active (Batch 7 — Finance-owned JWT config validation shipped and called by every service; per-endpoint RBAC live since Batch 4; Phase-1 permission auto-registration remains deferred)
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
- The `Finance.Gateway` MUST NOT call `AddWarehouseAuthentication` and MUST NOT validate JWT configuration — it never decodes tokens and forwards the `Authorization` header untouched (see SDD-INFRA-002 §2.5). JWT validation is exclusively a per-downstream-service concern.

### 2.2 Permission-based authorization (MUST)
- Every endpoint MUST be decorated with `[RequirePermission("finance.<resource>:<action>")]` from `Warehouse.Auth.AspNetCore`.
- Resource names MUST be lowercase, dot-separated; actions MUST be one of `read`, `write`, `delete`, `post`, `approve`.
- Permissions MUST be registered with auth-service on first run (see §2.4).
- For the shell milestone (Phase 0), permissions `finance.account:read` and `finance.account:write` are introduced.

### 2.3 Bearer token forwarding for downstream calls (MUST)
- When a Finance service calls auth-service for permission lookups, it MUST forward the inbound bearer token via `BearerTokenForwardingHandler` (registered automatically by `AddWarehousePermissionValidation`).
- This preserves the user identity across the call chain so auth-service can return the correct permission set.

### 2.4 Permission registration (DEFERRED — Phase 1)
- **Status: DEFERRED.** The auth-service permission-registration endpoint contract is not yet defined, so automatic registration is NOT implemented. Until that contract exists, permissions are seeded MANUALLY in auth-service. No Finance service performs startup permission registration today.
- When implemented (Phase 1), each Finance service MUST register its required permissions with auth-service on application startup via the auth-service permission-registration endpoint.
- Permission descriptors MUST include: `code` (`finance.<resource>:<action>`), `domain` (`finance`), and `description` (human-readable).
- Registration MUST be idempotent — repeated startups MUST NOT create duplicates.
- This deferral is intentional; promoting the spec to Active does NOT promote this rule. Tracking continues until the auth-service registration contract is published.

### 2.5 Token issuance (out of scope)
- Finance MUST NOT issue tokens. All login, refresh, and password operations go to auth-service through the Finance gateway.

## 3. Validation

### 3.1 Finance-owned JWT configuration validation (MUST)
- JWT configuration validation is OWNED BY FINANCE (not by the `Warehouse.Auth.AspNetCore` package). It is implemented as `ValidateFinanceJwtConfiguration(IConfiguration)` in `Finance.Infrastructure.Web` and MUST be called by EACH Finance microservice (`Accounts`, `Nomenclature`, `EventLog`) in `Program.cs`, right next to `AddWarehouseAuthentication(configuration)`.
- The validator MUST throw a clear, actionable startup error (e.g., `InvalidOperationException`) — failing fast before the application serves traffic — when ANY of the following holds:
  - `Jwt:SecretKey` is missing, or shorter than 32 characters.
  - `Jwt:Issuer` is missing or empty/whitespace.
  - `Jwt:Audience` is missing or empty/whitespace.
- When all three values are present and valid, the validator MUST complete without throwing.
- The `Finance.Gateway` MUST NOT call this validator (it does not decode or validate tokens — see §2.1).

## 4. Error Rules

| Code | HTTP | Meaning |
|---|---|---|
| `MISSING_TOKEN` | 401 | No bearer token in `Authorization` header |
| `INVALID_TOKEN` | 401 | Signature/issuer/audience/lifetime check failed |
| `INSUFFICIENT_PERMISSIONS` | 403 | Authenticated but lacks required `finance.<resource>:<action>` |

All emitted as ProblemDetails with `title` = the code and `type` = `https://finance.local/errors/{code}`.

## 5. Versioning

This contract is versioned together with the `Warehouse.Auth.AspNetCore` NuGet package. Breaking changes to JWT claims or the permission attribute trigger a major version bump in that package; Finance services pin to compatible major versions via `0.1.*` floating ranges until the package reaches 1.0.

### Versioning Notes

- **v1 — Initial specification (Draft).** Federated JWT validation via `AddWarehouseAuthentication`, per-endpoint `[RequirePermission]` RBAC, bearer-token forwarding, Phase-1 permission auto-registration (deferred at shell), and a stated requirement that `Jwt:SecretKey` ≥ 32 chars with non-empty issuer/audience.
- **v2 — Batch 7 (Active, non-breaking).** Recorded that JWT configuration validation is OWNED BY FINANCE via `ValidateFinanceJwtConfiguration(IConfiguration)` in `Finance.Infrastructure.Web`, called by every service (`Accounts`, `Nomenclature`, `EventLog`) next to `AddWarehouseAuthentication`. Clarified that the gateway does NOT validate JWT (SDD-INFRA-002 §2.5). Restated Phase-1 permission auto-registration as explicitly DEFERRED (auth-service registration contract undefined; permissions seeded manually). Non-breaking: no change to token format, claims, or the RBAC attribute.

## 6. Test Plan

JWT-validation unit tests live alongside the validator in `Finance.Infrastructure.Web` test coverage and carry `[Category("SDD-INT-AUTH-001")]`. RBAC tests that need a REAL auth-service permission lookup (403 path) require external infrastructure and carry the excluded `[Category("Integration")]` marker.

| Test name | Kind |
|---|---|
| `ValidateFinanceJwtConfiguration_Throws_WhenSecretKeyMissing` | [Unit] |
| `ValidateFinanceJwtConfiguration_Throws_WhenSecretKeyShorterThan32Chars` | [Unit] |
| `ValidateFinanceJwtConfiguration_Throws_WhenIssuerEmpty` | [Unit] |
| `ValidateFinanceJwtConfiguration_Throws_WhenAudienceEmpty` | [Unit] |
| `ValidateFinanceJwtConfiguration_Succeeds_WhenAllValuesPresentAndValid` | [Unit] |
| `AddWarehouseAuthentication_RejectsInvalidIssuer` | [Integration] |
| `RequirePermission_ReturnsForbidden_WhenPermissionMissing` | [Integration] |
| `RequirePermission_AllowsRequest_WhenPermissionPresent` | [Integration] |
| `BearerTokenForwarding_PreservesTokenAcrossDownstreamCalls` | [Integration] |

## 7. Open Items

- Service-to-service (S2S) JWT issuance for Finance → Warehouse calls. Current placeholder: a static `service:finance-integration` permission and a long-lived token minted out-of-band. Final design tracked in `CHG-FEAT-001`.
- JWKS-based key rotation. Today we ship a symmetric `Jwt:SecretKey`; rotation requires coordinated updates across all services. Asymmetric (RS256) is on the roadmap.
