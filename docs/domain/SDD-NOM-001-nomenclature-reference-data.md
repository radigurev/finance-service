# SDD-NOM-001 — Nomenclature Reference Data

> Status: Implemented (Batch 5 — currency CRUD + exchange-rate read + Warehouse country/state/city proxy ship; rate WRITE/BNB import, S2S JWT, and the React `useNomenclature()` hook are explicitly deferred)
> Owner: Platform
> Last updated: 2026-05-30
> Category: Domain
> Service: `Finance.Nomenclature.API` — port **6009**
> Related: SDD-INFRA-001 (correlation, ProblemDetails, ProblemDetails factory), SDD-INFRA-004 (cache), SDD-INFRA-005 (filtering/paging), SDD-INFRA-006 (outbox + idempotency), SDD-INFRA-007 (validation chain), SDD-INFRA-009 (base service/controller), SDD-AUDIT-001 (audit-first), SDD-OBS-001 (observability), SDD-INT-AUTH-001 (RBAC), SDD-ACCT-001 (canonical service mirrored)
> Mirrors: Warehouse `SDD-NOM-001`; structurally mirrors `Finance.Accounts.API` (the canonical Batch-4 reference service)

---

## 1. Context & Scope

This spec defines `Finance.Nomenclature.API` (port **6009**), the shared reference-data service for Finance. It owns the canonical lookup tables that every other Finance service references but no other service mutates: currencies and currency exchange rates. Countries, state/provinces, and cities are NOT owned here — they are proxied from Warehouse Nomenclature.

**ISA-95 classification.** `Currency` and `ExchangeRate` are ISA-95 **Level 4 (Business Planning & Logistics)** reference/master-data entities (ISA-95 / IEC 62264 Part 1, Level 4). Their create / update / deactivate operations are reference-data maintenance operations — they emit immutable domain events for state changes (SDD-INFRA-006) but do NOT constitute a business transaction. No production, scheduling, or Level 3 (MES) activity is modelled here.

For the BG-first deployment, the Finance system already uses **Warehouse's** Nomenclature service for countries / state-provinces / cities (those records describe a counterparty's address — the same address Warehouse already stores for the same legal entity). Finance therefore CONSUMES Warehouse's nomenclature via a Refit client through the Warehouse Gateway; only finance-specific reference data (currencies + their exchange rates) lives natively in `Finance.Nomenclature.API`.

**Table ownership (Batch-5 resolved):**
- `Finance.Nomenclature.API` OWNS BOTH the `Currency` AND the `ExchangeRate` tables in the `nomenclature` schema. The originally-planned `Finance.Currency.API` (SDD-FIN-005 Multi-Currency Engine) is **out of scope** for the foreseeable future, so its read tables are absorbed here rather than left unowned.
- The exchange-rate WRITE side and BNB import (SDD-INT-BNB-001) are **DEFERRED**. Batch 5 ships exchange-rate READ endpoints only; rows are populated externally (manual seed / future BNB job) until the write path lands.

**In scope (Batch 5 — ships Active):**
- Currencies (ISO 4217) — create / read / update, soft-delete via `IsActive` (NO hard DELETE)
- Exchange rates per currency per date — READ-side query only (latest-on-or-before and range)
- The `IWarehouseNomenclatureClient` Refit interface for countries/states/cities — **defined in THIS spec** because `SDD-INT-WH-002` (the general Finance → Warehouse Refit client spec) is not yet drafted. When SDD-INT-WH-002 is drafted it will subsume the cross-cutting handler-chain conventions; the contract itself stays here.
- Country/State/City proxy endpoints backed by the Refit client, cached 30 min per query
- Redis caching of currency list + full active list (reference data); exchange-rate reads are NOT cached (SDD-INFRA-004)
- Database seeding (ISO 4217 currency list, ~180 entries) gated by `EnableCurrencySeeding` feature flag
- Currency mutations are audited (SDD-AUDIT-001) and publish domain events via the transactional outbox (SDD-INFRA-006)

**Deferred (NOT in this batch):**
- Exchange-rate WRITE endpoints and BNB CSV/feed import — SDD-INT-BNB-001 / future `CHG-FEAT-*`
- Service-to-service (S2S) JWT for the Warehouse proxy. Batch 5 **forwards the inbound bearer token** on outbound Refit calls instead. A dedicated S2S handler arrives with SDD-INT-WH-002.
- The shared `useNomenclature()` React hook and cascading dropdown wiring — **deferred to the frontend batch (Batch 8)**. No frontend is built in this batch.

**Out of scope (permanently / by another spec):**
- Address-format localization (no formatting rules)
- Postal code validation
- Owning the country / state / city catalogue — that stays in Warehouse Nomenclature
- Tax rates (those are country-strategy concerns, see SDD-CTRY-001)

**Related specs:**
- `SDD-CTRY-BG-001` — Bulgaria strategy seeds `BGN`, `EUR`, `USD` as default visible currencies and configures BNB as the rate provider
- `SDD-INT-WH-002` — Finance → Warehouse Refit client (NOT yet drafted; the `IWarehouseNomenclatureClient` contract lives here until then)
- `SDD-INT-BNB-001` — BNB exchange-rate provider (rate write/import deferred to this spec)
- `SDD-FIN-005` — Multi-Currency Engine (Currency service out of scope; its read tables absorbed here)
- `SDD-INV-001` (Invoices) and `SDD-PAY-001` (Payments) reference currencies on every line and amount

## 2. Behavior

### 2.0 Data model (MUST)
- The `Currency` entity MUST have: `Id` (`INT IDENTITY`, PK — internal), `IsoCode` (3-char, **unique**), `Name`, `Symbol`, `IsActive`.
- The `ExchangeRate` entity MUST have: `Id` (`INT IDENTITY`, PK), `CurrencyIsoCode`, `Rate` (`DECIMAL(18,6)`), `RateDate` (`DATETIMEOFFSET`), with a **unique** constraint on (`CurrencyIsoCode`, `RateDate`).
- Both tables MUST live in the `nomenclature` schema, configured via EF Fluent API only (no Data Annotations). `NomenclatureDbContext` MUST implement `IAuditDbContext` and register the MassTransit outbox entities (for currency events).

### 2.1 Currency CRUD (MUST)
- `GET /api/v1/currencies` MUST list currencies through `SearchableServiceBase` (accepts a `FilterRequest`, returns `PagedResult<CurrencyDto>` per SDD-INFRA-005) with default order by `IsoCode`. The list MUST include both active and inactive currencies.
- The **simple full active-currency list** (used to populate dropdowns) MUST be cached at `finance-nomenclature:currencies:all` with TTL 30 min (reference data, SDD-INFRA-004).
- `GET /api/v1/currencies/{isoCode}` MUST return a single currency or `CURRENCY_NOT_FOUND` (404).
- `POST /api/v1/currencies` MUST create a currency (`IsoCode`, `Name`, `Symbol`, `IsActive`). Requires `finance.nomenclature:write`.
- `PUT /api/v1/currencies/{isoCode}` MUST update `Name`, `Symbol`, `IsActive`. `IsoCode` is **immutable**, enforced **structurally**: `UpdateCurrencyRequest` carries no `IsoCode` field, so the path `{isoCode}` is the only source of the code and the body cannot express a change (see §2.6).
- `DELETE` MUST NOT be exposed; deactivation is performed via `IsActive = false` (soft delete).
- Currency create / update / deactivate MUST be **audit-first** (SDD-AUDIT-001 — write the `audit.OperationsEvents` row in the same transaction, before outbox) and MUST publish a `CurrencyCreated` / `CurrencyUpdated` / `CurrencyDeactivated` event (in `Finance.ServiceModel/Events/Nomenclature/`, each `: IFinanceEvent`) through the transactional outbox (SDD-INFRA-006).
- On deactivation the audit `StateChange` row MUST record a **system-supplied standard deactivation reason** (`CurrencyAuditEventTypes.DefaultDeactivationReason`); the audit trail still captures who / when / what. A **caller-supplied** deactivation reason is NOT part of v1 — reference-data deactivation is not on SDD-AUDIT-001's mandatory caller-`Reason` list (period close, journal reversal, permission revocation), so omitting it is consistent. A caller-supplied reason is a future enhancement.
- Every successful write MUST invalidate the `finance-nomenclature:*` cache pattern.

### 2.2 Exchange rates — read (MUST)
- `GET /api/v1/exchange-rates?currency={iso}&date={yyyy-MM-dd}` MUST return the latest rate on or before the date.
- `GET /api/v1/exchange-rates?currency={iso}&from={d1}&to={d2}` MUST return the range ordered by `RateDate`.
- Both endpoints MUST validate that the currency exists (else `CURRENCY_NOT_FOUND`, 404).
- Caching: exchange-rate reads are **transactional reads** and MUST NOT be cached — these endpoints MUST hit the database every time (SDD-INFRA-004 prohibits caching transactional data).
- Exchange-rate WRITE / BNB import is DEFERRED (see Context); Batch 5 exposes READ only.

### 2.3 Country / State / City — proxied (MUST)
- `GET /api/v1/countries`, `/states?country={iso2}`, `/cities?stateId={id}` MUST proxy to Warehouse Nomenclature via the `IWarehouseNomenclatureClient` Refit interface (defined in this spec) calling the Warehouse Gateway base URL from `Warehouse:NomenclatureBaseUrl` config.
- The Refit client MUST be registered with a correlation-id delegating handler (reuse `Warehouse.Correlation` if it exposes one, otherwise a small `CorrelationIdDelegatingHandler` reading `ICorrelationIdAccessor`) AND `AddStandardResilienceHandler` (`Microsoft.Extensions.Http.Resilience`), per SDD-INFRA-001.
- **S2S JWT is DEFERRED**: until SDD-INT-WH-002 is drafted, outbound calls MUST forward the inbound caller's bearer token rather than mint a service token.
- Proxy results MUST be cached 30 min keyed by the query (e.g., `finance-nomenclature:countries:all`, `…:states:{iso2}`, `…:cities:{stateId}`).
- On upstream failure the proxy endpoints MUST return `503 WAREHOUSE_NOMENCLATURE_UNREACHABLE`.

### 2.4 Cascading dropdowns in SPA (DEFERRED — frontend Batch 8)
- The shared `useNomenclature()` React hook (planned for `frontend/src/shared/hooks/`) will expose `countries`, `getStates(countryIso)`, `getCities(stateId)`, `currencies`; forms MUST use it for counterparty addresses and MUST NOT hard-code dropdowns.
- This hook is **DEFERRED to the frontend batch (Batch 8)** and is NOT built in Batch 5. The backend endpoints above are the contract the hook will consume.

### 2.5 Database seeding (MUST when enabled)
- On startup, when feature flag `EnableCurrencySeeding == true` (read via `Microsoft.FeatureManagement`), the service MUST upsert all currencies from a built-in static ISO 4217 list (~180 entries). Existing rows MUST NOT be overwritten and currencies MUST NOT be removed.

### 2.6 Edge cases (MUST)
- **`IsoCode` immutability is structural** — `UpdateCurrencyRequest` exposes only `Name`, `Symbol`, `IsActive`, and `RowVersion`; it has no `IsoCode` member. The path `{isoCode}` is the sole authoritative source of the code, so a caller cannot express an IsoCode change. Immutability is therefore guaranteed by the request contract's shape rather than by a runtime rejection branch.
- **Stale `RowVersion` on update** — the update MUST carry the base64 `RowVersion` captured on the prior read; a stale or malformed token MUST yield `CONCURRENT_MODIFICATION` (409) via the concurrency check (see §3 / §4).
- **Re-activating a soft-deleted currency** — `PUT` with `IsActive = true` MUST reactivate the existing row (no duplicate), publish `CurrencyUpdated`, and invalidate the cache.
- **Exchange-rate `date` with no matching row** — `?date=` queries with no rate on or before the date MUST return `EXCHANGE_RATE_NOT_FOUND` (404), not an empty 200.
- **Redis unreachable on a cached read** — currency-list / proxy reads MUST fall through to the source (DB or upstream) and still succeed; service availability MUST NOT depend on Redis (SDD-INFRA-004).

## 3. Validation Rules

Shape-only rules MUST live in FluentValidation with `.WithErrorCode(...)` referencing constants in `Finance.Common.ErrorCodes.NomenclatureErrorCodes` / `CommonErrorCodes`. Stateful/uniqueness rules MUST run through an `IChainValidator` (SDD-INFRA-007).

### Field-level (FluentValidation)
| Field | Rule | Error code |
|---|---|---|
| `IsoCode` | exactly 3 uppercase letters (`^[A-Z]{3}$`) | `INVALID_CURRENCY_CODE` |
| `Name` | NotEmpty, MaxLength 100 | `INVALID_CURRENCY_NAME` |
| `Symbol` | MaxLength 5 | `INVALID_CURRENCY_SYMBOL` |

### Cross-field / stateful
| Rule | Mechanism | Error code |
|---|---|---|
| `IsoCode` MUST be unique across currencies (create) | `IChainValidator` (DB lookup) | `DUPLICATE_CURRENCY_CODE` (409) |
| Exchange-rate range query: `from` MUST be ≤ `to` | request validation | `INVALID_DATE_RANGE` (400) |
| Exchange-rate query currency MUST exist | service-side lookup | `CURRENCY_NOT_FOUND` (404) |
| Currency update MUST carry the base64 `RowVersion` from the prior read; a stale or malformed token MUST be rejected | optimistic concurrency via `SaveWithConcurrencyCheck` (SDD-INFRA-009) | `CONCURRENT_MODIFICATION` (409) |

## 4. Error Rules

All error responses MUST be RFC-7807 ProblemDetails produced by the customized factory (SDD-INFRA-001): `title` = machine code (SCREAMING_SNAKE_CASE), `detail` = developer English, `type` = `https://finance.local/errors/{code}`. Validation responses carry codes in the `errors` dictionary. Each code MUST get a matching `errors.<CODE>` entry in `frontend/src/shared/i18n/locales/{en,bg}.ts` when the frontend (Batch 8) is built.

| Code | HTTP | Trigger | Type |
|---|---|---|---|
| `INVALID_CURRENCY_CODE` | 400 | `IsoCode` not 3 uppercase letters | validation |
| `INVALID_CURRENCY_NAME` | 400 | `Name` empty or > 100 chars | validation |
| `INVALID_CURRENCY_SYMBOL` | 400 | `Symbol` > 5 chars | validation |
| `INVALID_DATE_RANGE` | 400 | Exchange-rate `from` > `to` | validation |
| `DUPLICATE_CURRENCY_CODE` | 409 | `IsoCode` already exists (chain validator) | conflict |
| `CONCURRENT_MODIFICATION` | 409 | Stale or malformed `RowVersion` on currency update | conflict (concurrency) |
| `CURRENCY_NOT_FOUND` | 404 | No matching currency row | not found |
| `EXCHANGE_RATE_NOT_FOUND` | 404 | No rate on/before the requested date | not found |
| `WAREHOUSE_NOMENCLATURE_UNREACHABLE` | 503 | Country/state/city upstream proxy down — frontend SHOULD allow free-text fallback for non-critical fields | upstream unavailable |

Services MUST return `Result<T>` (never throw for business failures); `BaseApiController.ToActionResult` maps codes to HTTP via `IErrorCodeToStatusMap` (SDD-INFRA-009). Constants live in `Finance.Common.ErrorCodes.NomenclatureErrorCodes` (and `CommonErrorCodes` for shared codes — `CONCURRENT_MODIFICATION` lives in `CommonErrorCodes`).

## 5. Versioning Notes

**v1 — Initial specification (Batch 5, 2026-05-30).** Ships `Finance.Nomenclature.API` on port **6009**: currency create/read/update + soft-delete, exchange-rate READ (latest-on-or-before + range), Warehouse country/state/city proxy. Mirrors the canonical `Finance.Accounts.API`. New endpoints (e.g., tax-jurisdiction catalogues) are additive (non-breaking). Removing a currency from the seed list is NOT permitted (historical invoices reference it).

**Batch-5 resolved decisions (recorded for traceability):**
1. `Finance.Nomenclature.API` OWNS both the `Currency` and `ExchangeRate` tables — `Finance.Currency.API` (SDD-FIN-005) is out of scope, so its read tables are absorbed here. (non-breaking against the original plan; clarifies ownership)
2. Exchange-rate WRITE / BNB import (SDD-INT-BNB-001) is DEFERRED — Batch 5 exposes READ only.
3. The `IWarehouseNomenclatureClient` Refit contract is defined in THIS spec because SDD-INT-WH-002 is not yet drafted.
4. S2S JWT for the Warehouse proxy is DEFERRED — the inbound bearer token is forwarded for now.
5. Currency-list / full-active-list reads are cached; exchange-rate reads are uncached (transactional).
6. ISO-4217 seeding is gated by the `EnableCurrencySeeding` feature flag (`Microsoft.FeatureManagement`).
7. Currency mutations are audited (audit-first) and publish events via the transactional outbox.
8. The React `useNomenclature()` hook is DEFERRED to the frontend batch (Batch 8).

These decisions resolve the corresponding Open Items below; remaining open items are genuinely future work.

## 6. Test Plan

Tests carry `[Category("SDD-NOM-001")]`. Per the environment constraint (no Docker/SQL/Redis/RabbitMQ/Warehouse), only `[Unit]` tests run by default — EF unit tests use SQLite in-memory and the Warehouse Refit client, cache, and publisher are mocked. The `[Integration]` tests below are marked `[Category("Integration")]` and excluded from the default run.

### Unit (run in this environment)
| Test | Kind |
|---|---|
| `CurrencyValidator_RejectsLowercaseCode_ReturnsInvalidCurrencyCode` | [Unit] |
| `CurrencyValidator_RejectsCodeNotThreeLetters_ReturnsInvalidCurrencyCode` | [Unit] |
| `CurrencyValidator_RejectsEmptyName_ReturnsInvalidCurrencyName` | [Unit] |
| `CurrencyValidator_RejectsSymbolOverFiveChars_ReturnsInvalidCurrencySymbol` | [Unit] |
| `DuplicateCurrencyCodeValidator_ExistingIso_ReturnsDuplicateCurrencyCode` | [Unit] |
| `SearchAsync_DefaultOrder_ReturnsCurrenciesOrderedByIsoCode` | [Unit] |
| `SearchAsync_ReturnsActiveAndInactive` | [Unit] |
| `GetByIsoCodeAsync_UnknownCode_ReturnsCurrencyNotFound` | [Unit] |
| `UpdateAsync_ReactivatesSoftDeletedCurrency_PublishesUpdatedEvent` | [Unit] |
| `UpdateAsync_StaleRowVersion_ReturnsConcurrentModification` | [Unit] |
| `CreateAsync_Valid_RecordsAuditBeforeOutboxAndInvalidatesCache` | [Unit] |
| `DeactivateAsync_RecordsSystemSuppliedDeactivationReason` | [Unit] |
| `GetLatestRateAsync_NoRateOnOrBeforeDate_ReturnsExchangeRateNotFound` | [Unit] |
| `GetRateRangeAsync_FromAfterTo_ReturnsInvalidDateRange` | [Unit] |
| `GetRateRangeAsync_ReturnsRatesOrderedByRateDate` | [Unit] |
| `GetLatestRateAsync_DoesNotCallCache` | [Unit] |
| `Seeder_SkipsExistingRows_DoesNotOverwrite` | [Unit] |
| `Seeder_DisabledByFeatureFlag_DoesNotSeed` | [Unit] |
| `WarehouseProxy_ForwardsInboundBearerToken_OnOutboundCall` | [Unit] |

### Integration (`[Category("Integration")]`, excluded from default run)
| Test | Kind |
|---|---|
| `ListCurrencies_ReturnsActiveAndInactive_OrderedByIsoCode` | [Integration] |
| `ListCurrencies_FullActiveList_ServedFromRedis_OnSecondCall` | [Integration] |
| `CreateCurrency_PersistsRowAndInvalidatesCache` | [Integration] |
| `CreateCurrency_Returns409_OnDuplicateIso` | [Integration] |
| `UpdateCurrency_IsoCodeFromPathOnly_BodyCannotChangeIt` | [Integration] |
| `UpdateCurrency_Returns409_OnStaleRowVersion` | [Integration] |
| `GetExchangeRate_HitsDatabase_NotCache` | [Integration] |
| `Seeder_UpsertsAllIso4217_OnFirstStartup` | [Integration] |
| `Seeder_SkipsExistingRows` | [Integration] |
| `WarehouseProxy_CountriesEndpoint_ReturnsResponseFromUpstream` | [Integration] |
| `WarehouseProxy_ReturnsCachedResponse_OnSecondCall` | [Integration] |
| `WarehouseProxy_Returns503_WhenUpstreamUnreachable` | [Integration] |
| `CreateCurrency_Returns403_WhenPermissionMissing` | [Integration] |

## 7. Open Items

Resolved in Batch 5 (see §5 Versioning Notes): table ownership (Currency + ExchangeRate owned here), rate-write deferral, `IWarehouseNomenclatureClient` home, S2S-JWT deferral, caching policy, seeding flag, audit + outbox on mutations, `useNomenclature()` hook deferral.

Still open:
- Whether Finance ever owns its own country catalogue instead of proxying Warehouse. Current choice: proxy. Revisit only if Finance ships standalone (without Warehouse).
- Multi-language currency `Name` (BG + EN) — defer until SDD-UI shows it's needed.
- Exchange-rate WRITE path + historical BNB import (one-off CSV / scheduled feed) — `SDD-INT-BNB-001` / separate `CHG-FEAT-*`.
- Promoting the `IWarehouseNomenclatureClient` contract and the standard handler chain (correlation → S2S JWT → resilience) into `SDD-INT-WH-002` once that spec is drafted.
