# SDD-INFRA-004 — Redis Distributed Cache

> Status: Planned
> Owner: Platform
> Last updated: 2026-05-28
> Category: Infrastructure
> Related: SDD-INFRA-001, SDD-NOM-001, SDD-ACCT-001, SDD-AUDIT-001
> Mirrors: Warehouse `SDD-INFRA-001` §Caching

---

## 1. Context & Scope

This spec defines the Redis distributed-cache layer used by every Finance microservice. Redis is provided by the shared `platform_net` Docker network (the same instance Warehouse uses); Finance does NOT run a separate Redis instance. The shared `Finance.Infrastructure.Caching` package wraps `IDistributedCache` with an `ICacheService<T>` that handles JSON serialization, TTL conventions, and cache-key naming.

**In scope:**
- `ICacheService<T>` with `GetOrSetAsync`, `RemoveAsync`, `RemoveByPatternAsync`
- DI extension `services.AddFinanceRedisCache(configuration)`
- Standard key convention: `{service}:{entity}:all` for full-list reads, `{service}:{entity}:{id}` for single-row reads
- TTL conventions per data class (see §2.2)
- Cache-aside pattern for reference data (chart of accounts, currencies, periods, posting rules, tax rates)
- Cache invalidation on every write (the service that owns the data MUST invalidate its own keys)
- Pub/sub for cross-service invalidation (e.g., when CoA is updated, every reader-side cache is told to evict)

**Out of scope:**
- Caching transactional data (journal entries, invoices, payments, balances) — **forbidden**
- Session storage (JWT is stateless; refresh tokens live in auth-service)
- Rate-limit backend (handled at the Finance.Gateway level natively)
- Caching cross-service data fetched via Refit clients (those have their own short-TTL cache, ≤ 60 s)

## 2. Behavior

### 2.1 Cache-key convention (MUST)
- `{service}:{entity}:all` — full collection read (e.g., `finance-accounts:chart:all`).
- `{service}:{entity}:{id}` — single row by primary key.
- `{service}:{entity}:byCode:{code}` — single row by natural key.
- `{service}:{entity}:filter:{stableHash}` — filtered list, where `stableHash` is SHA-256 over a canonical query-string ordering.
- Service prefixes are kebab-case and stable: `finance-accounts`, `finance-currency`, `finance-periods`, `finance-nomenclature`.

### 2.2 TTL conventions (MUST)
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
- Bulk invalidation MUST use `RemoveByPatternAsync` (Lua `SCAN` on the Redis side); pattern key MUST NOT be unbounded (always prefixed by `{service}:`).

### 2.4 Cross-service invalidation (SHOULD — Phase 5)
- When a domain that's referenced by another service changes (e.g., a currency is deactivated), a `<Entity>InvalidatedEvent` MUST be published on the bus and a generic consumer in the consuming service evicts its local cache entry. Naïve TTL eventual consistency is acceptable for v1.

### 2.5 Failure mode (MUST)
- If Redis is unreachable, the cache layer MUST fall through to the underlying repository call (log a warning, never throw). Service availability MUST NOT depend on Redis.

## 3. Validation Rules

- Cache key MUST start with a registered `service` prefix.
- TTL MUST be ≥ 1 second and ≤ 24 hours.
- Cache configuration validation at startup: `ConnectionStrings:Redis` MUST be present and resolvable.

## 4. Error Rules

| Code | HTTP | Trigger |
|---|---|---|
| `REDIS_UNREACHABLE` | (logged, not returned) | Connection failure — falls through to DB |
| `CACHE_KEY_PATTERN_VIOLATION` | 500 | Service tried to use a key not prefixed by its registered service name |

## 5. Versioning Notes

v1: cache-aside only. v2 (deferred) will introduce write-through caching for hot single-row reads (e.g., the chart of accounts head).

## 6. Test Plan

| Test | Kind |
|---|---|
| `GetOrSetAsync_ReturnsFromCache_OnSecondCall` | [Integration, Testcontainers Redis] |
| `GetOrSetAsync_FallsThroughToFactory_WhenRedisDown` | [Integration] |
| `RemoveAsync_EvictsKey` | [Integration] |
| `RemoveByPatternAsync_EvictsAllMatching_DoesNotEvictOthers` | [Integration] |
| `Write_InvalidatesAllListKeys` | [Integration] |
| `KeyPattern_RejectsKeyWithoutServicePrefix` | [Unit] |
| `Configuration_FailsStartup_WhenRedisConnectionMissing` | [Unit] |

## 7. Open Items

- Adopt MessagePack instead of JSON for hot paths (chart of accounts read ~100×/sec)? Benchmark in Phase 7.
- Consider Redis Streams instead of an explicit `<Entity>InvalidatedEvent` for cross-service cache invalidation. Deferred until needed.
