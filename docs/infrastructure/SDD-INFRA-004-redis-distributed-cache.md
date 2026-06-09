# SDD-INFRA-004 — Redis Distributed Cache

> Status: Implemented (library: `ICacheService<T>`, `RedisCacheService`, `AddFinanceRedisCache`, cache-aside v1). Deferred: cross-service pub/sub invalidation (Phase 5).
> Owner: Platform
> Last updated: 2026-05-30
> Category: Infrastructure
> Related: SDD-INFRA-001, SDD-INFRA-006 (reuses this library's Redis connection), SDD-NOM-001, SDD-ACCT-001, SDD-AUDIT-001
> Mirrors: Warehouse `SDD-INFRA-001` §Caching
> Implementation: `src/Infrastructure/Caching/Finance.Infrastructure.Caching/` (references `Finance.Common`)

---

## 1. Context & Scope

This spec defines the Redis distributed-cache layer used by every Finance microservice. Redis is provided by the shared `platform_net` Docker network (the same instance Warehouse uses); Finance does NOT run a separate Redis instance. The shared `Finance.Infrastructure.Caching` package wraps **StackExchange.Redis `IConnectionMultiplexer`** with an `ICacheService<T>` that handles JSON serialization (System.Text.Json for v1), TTL conventions, and cache-key naming. The multiplexer is used directly (not `IDistributedCache`) because `RemoveByPatternAsync` requires multiplexer-level `SCAN`.

### Batch-3 resolved decisions

- **Library location:** `src/Infrastructure/Caching/Finance.Infrastructure.Caching/`, references `Finance.Common`. Built with `dotnet build` on its own `.csproj`; it does **not** add itself to `src/Finance.slnx` (the Integrate step does that).
- **Connection backbone:** `RedisCacheService` is implemented over StackExchange.Redis `IConnectionMultiplexer` (package line `8.0.x`). `RemoveByPatternAsync` uses multiplexer-level `SCAN`, **bounded by a `{service}:` prefix — never an unbounded scan**.
- **Serialization:** `System.Text.Json` for v1. (MessagePack is a deferred open item.)
- **Connection ownership:** this library **OWNS** the Redis `IConnectionMultiplexer` registration (lazy). `Finance.Infrastructure.Messaging` (SDD-INFRA-006) **reuses** that same multiplexer for its idempotency filter — it does not register its own.
- **Cross-service pub/sub invalidation is deferred to Phase 5** (§2.4). v1 relies on TTL eventual consistency plus owner-side invalidation.
- Error-code constants are referenced from `Finance.Common.ErrorCodes.CachingErrorCodes` (`REDIS_UNREACHABLE`, `CACHE_KEY_PATTERN_VIOLATION`) — never raw strings.

**In scope:**
- `ICacheService<T>` with `GetOrSetAsync(key, factory, ttl?, ct)`, `RemoveAsync(key, ct)`, `RemoveByPatternAsync(prefixedPattern, ct)`
- `RedisCacheService` over StackExchange.Redis `IConnectionMultiplexer`; System.Text.Json serialization (v1)
- DI extension `services.AddFinanceRedisCache(configuration)` — registers `IConnectionMultiplexer` (lazy) + `ICacheService<>`; startup validates `ConnectionStrings:Redis`
- Standard key convention: `{service}:{entity}:all`, `:{id}`, `:byCode:{code}`, `:filter:{sha256}` (see §2.1)
- TTL conventions per data class (see §2.2)
- Cache-aside pattern (v1 only) for reference data (chart of accounts, currencies, periods, posting rules, tax rates)
- Cache invalidation on every write (the service that owns the data MUST invalidate its own keys)

**Out of scope:**
- Caching transactional data (journal entries, invoices, payments, balances) — **forbidden**
- Session storage (JWT is stateless; refresh tokens live in auth-service)
- Rate-limit backend (handled at the Finance.Gateway level natively)
- Caching cross-service data fetched via Refit clients (those have their own short-TTL cache, ≤ 60 s)
- **Cross-service pub/sub invalidation — deferred to Phase 5** (§2.4)
- Write-through caching — deferred to v2 (§5)

## 2. Behavior

### 2.1 Cache-key convention (MUST)
- `{service}:{entity}:all` — full collection read (e.g., `finance-accounts:chart:all`).
- `{service}:{entity}:{id}` — single row by primary key.
- `{service}:{entity}:byCode:{code}` — single row by natural key.
- `{service}:{entity}:filter:{stableHash}` — filtered list, where `stableHash` is SHA-256 over a canonical query-string ordering.
- Service prefixes are kebab-case and stable: `finance-accounts`, `finance-currency`, `finance-periods`, `finance-nomenclature`, `finance-journal` (the journal service caches posting rules as reference data — `finance-journal:posting-rule:*`; registered per `CHG-FIX-003`). Adding a new cache-consuming service MUST register its prefix in `FinanceCacheOptions.RegisteredServicePrefixes`.

### 2.2 TTL conventions (MUST)
These are the v1 default TTLs the library applies when a caller does not pass an explicit `ttl`. Defaults: reference data 30 min, permissions 5 min, latest rates 5 min, cross-service reads 60 s.

| Data class | TTL | Examples |
|---|---|---|
| Permissions | 5 min | Per-user permission lookups (SDD-INT-AUTH-001) |
| Reference data | 30 min | Chart of accounts, currencies, posting-rule templates, fiscal periods |
| Cross-service reads (Refit) | 60 s | Customer / product lookups from Warehouse |
| Exchange rates (latest per currency) | 5 min | BNB rates rotation window |
| **Transactional data** | **MUST NOT cache** | Journal entries, account balances, invoice totals, payments |

### 2.3 Invalidation (MUST)
- Every write operation MUST invalidate matching cache keys before returning.
- Invalidation MUST happen inside the same logical operation as the write (after `SaveChanges`, before HTTP response).
- Bulk invalidation MUST use `RemoveByPatternAsync`, which runs a multiplexer-level `SCAN` over the StackExchange.Redis `IConnectionMultiplexer`; the pattern MUST NOT be unbounded (always prefixed by `{service}:`).

### 2.4 Cross-service invalidation (SHOULD — deferred to Phase 5)
- **Deferred to Phase 5.** When a domain that's referenced by another service changes (e.g., a currency is deactivated), a `<Entity>InvalidatedEvent` will be published on the bus and a generic consumer in the consuming service evicts its local cache entry. Naïve TTL eventual consistency is the **v1 behavior** and is acceptable until Phase 5.

### 2.5 Failure mode (MUST)
- If Redis is unreachable, the cache layer MUST fall through to the factory / underlying repository call. It MUST log a warning via `ILogger` (structured template, code `CachingErrorCodes.REDIS_UNREACHABLE`) and **MUST NEVER throw**. Service availability MUST NOT depend on Redis.

## 3. Validation Rules

- Cache key MUST start with a registered `service` prefix; otherwise throw with `CachingErrorCodes.CACHE_KEY_PATTERN_VIOLATION`.
- TTL MUST be within `[1 second, 24 hours]`.
- `RemoveByPatternAsync` pattern MUST be prefixed by a registered `{service}:` segment (no unbounded scan).
- Cache configuration validation at startup: `ConnectionStrings:Redis` MUST be present and resolvable.

## 4. Error Rules

| Code | HTTP | Trigger |
|---|---|---|
| `REDIS_UNREACHABLE` | (logged, not returned) | Connection failure — `ILogger` warning, falls through to factory/DB, never throws |
| `CACHE_KEY_PATTERN_VIOLATION` | 500 | Service tried to use a key not prefixed by its registered service name |

Constants live in `Finance.Common.ErrorCodes.CachingErrorCodes`.

## 5. Versioning Notes

v1: cache-aside only, System.Text.Json serialization, owner-side invalidation + TTL eventual consistency. Cross-service pub/sub invalidation (§2.4) is **deferred to Phase 5**. v2 (deferred) will introduce write-through caching for hot single-row reads (e.g., the chart of accounts head). MessagePack serialization is a deferred open item (§7).

## 6. Test Plan

Batch-3 unit tests live in `src/Infrastructure/Finance.Infrastructure.Tests`. Tests that exercise a real Redis instance / `IConnectionMultiplexer` `SCAN` are `[Category("Integration")]` and excluded from the default offline run (no Redis available locally). Key-validation, TTL-bounds, fall-through, and startup-config tests run by default as `[Unit]` (Redis multiplexer mocked / connection-failure simulated).

| Test | Kind |
|---|---|
| `GetOrSetAsync_FallsThroughToFactory_WhenRedisDown` | [Unit] (multiplexer connection failure simulated) |
| `KeyPattern_RejectsKeyWithoutServicePrefix` | [Unit] |
| `Ttl_OutsideBounds_IsRejected` | [Unit] |
| `RemoveByPatternAsync_RejectsUnboundedPattern_WithoutServicePrefix` | [Unit] |
| `Configuration_FailsStartup_WhenRedisConnectionMissing` | [Unit] |
| `GetOrSetAsync_ReturnsFromCache_OnSecondCall` | [Integration] (real Redis) |
| `RemoveAsync_EvictsKey` | [Integration] (real Redis) |
| `RemoveByPatternAsync_EvictsAllMatching_DoesNotEvictOthers` | [Integration] (real Redis SCAN) |
| `Write_InvalidatesAllListKeys` | [Integration] (real Redis) |

## 7. Open Items

- Adopt MessagePack instead of JSON for hot paths (chart of accounts read ~100×/sec)? Benchmark in Phase 7.
- Consider Redis Streams instead of an explicit `<Entity>InvalidatedEvent` for cross-service cache invalidation. Deferred until needed.
