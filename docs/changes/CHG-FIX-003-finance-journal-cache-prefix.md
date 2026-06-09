# CHG-FIX-003 — `finance-journal` cache prefix unregistered → posting-rule writes 500

> Created: 2026-06-09
> Author: integration-test hardening (Batch 15 — offline gate)
> Status: Implemented
> Related specs: SDD-INFRA-004 (Redis Distributed Cache — authoritative), SDD-FIN-006 (Posting Engine + Posting Rules — consumer)
> Originating ticket: discovered by the new Posting endpoint integration suite

---

## 1. Summary

`PostingRuleService` caches posting rules (reference data) under the key region `finance-journal:posting-rule:*` and invalidates it on every write. `CacheKeyValidator` rejects any key/pattern not prefixed by a registered `{service}:` segment from `FinanceCacheOptions.RegisteredServicePrefixes`, and that default list did **not** include `finance-journal`. A posting-rule create/update therefore threw `CacheKeyPatternViolationException` during invalidation — **after** the row + audit had committed — surfacing as `500 GENERIC_ERROR`.

## 2. Motivation / root cause

The Journal service is the first cache consumer outside the originally-seeded set (`finance-accounts`, `finance-currency`, `finance-periods`, `finance-nomenclature`); its prefix was never added. The defect was invisible to unit tests, which construct `CacheKeyValidator` with a test-specific options instance rather than the production default.

## 3. Scope

### In scope
- Add `finance-journal` to `FinanceCacheOptions.RegisteredServicePrefixes` (the canonical default registry).

### Out of scope (explicit)
- The cache-aside, TTL, and bounded-pattern rules (unchanged).
- Broader Redis-down resilience (SDD-INFRA-004 already specifies fall-through-to-DB on connectivity failure; this defect was a key-validation error, not a connectivity failure — see Risks).

## 4. Behavior (Implemented — testable rules)

- `finance-journal:*` cache keys and scan patterns MUST validate successfully against the registered service prefixes.
- A posting-rule create/update MUST persist, audit, and invalidate its cache region and return success (no `500` from cache validation).

## 5. Affected specs / code

| Spec / file | Change |
|---|---|
| `SDD-INFRA-004` §2.1/§3 | Note `finance-journal` is a registered service segment. |
| `src/Infrastructure/Caching/Finance.Infrastructure.Caching/Configuration/FinanceCacheOptions.cs` | Add `finance-journal` to the default `RegisteredServicePrefixes`. |

## 6. Testing

- Integration (real Redis, Testcontainers): `PostingEndpointIntegrationTests` posting-rule create path (`SDD-FIN-006`) — green post-fix.
- Existing `CacheKeyValidator` unit tests unaffected (own options instance).

## 7. Risks / follow-up

- **Connectivity resilience already exists — no further work needed.** The SDD-INFRA-004 §2.5 "availability MUST NOT depend on Redis" invariant is already honored on the write/invalidation path: `RedisCacheService.RemoveByPatternAsync` and `RemoveAsync` catch `RedisException` / `RedisTimeoutException`, log `REDIS_UNREACHABLE`, and never rethrow. This defect was **not** an availability failure — it was a `CacheKeyPatternViolationException` (a *programming* error: an unregistered prefix), thrown by key validation *before* the resilient try/catch. Such validation errors are intentionally NOT swallowed (they must fail loudly in tests rather than hide a real prefix bug); the correct fix is registration (this change), guarded by `CacheKeyValidatorTests` cases for `finance-journal:posting-rule:{all,*}`.

## 8. Status

Implemented and verified. No migration, no API/event contract change.
