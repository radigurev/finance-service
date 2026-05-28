# SDD-NOM-001 — Nomenclature Reference Data

> Status: Planned
> Owner: Platform
> Last updated: 2026-05-28
> Category: Domain
> Related: SDD-INFRA-001, SDD-INFRA-004 (cache), SDD-ACCT-001, SDD-CUST counterparties (future), SDD-CTRY-001 (future)
> Mirrors: Warehouse `SDD-NOM-001`

---

## 1. Context & Scope

This spec defines `Finance.Nomenclature.API`, the shared reference-data service for Finance. It owns the canonical lookup tables that every other Finance service references but no other service mutates: countries, state/provinces, cities, currencies, currency exchange rates (read), and the country-aware account-type metadata.

For the BG-first deployment, the Finance system already uses **Warehouse's** Nomenclature service for countries / state-provinces / cities (those records describe a counterparty's address — the same address Warehouse already stores for the same legal entity). Finance therefore initially CONSUMES Warehouse's nomenclature via a Refit client through the Warehouse Gateway; only finance-specific reference data (currencies + their exchange rates) lives natively in `Finance.Nomenclature.API`.

**In scope:**
- Currencies (ISO 4217) — CRUD, soft-delete (`IsActive`)
- Exchange rates per currency per date — read-side query; write side comes from `Finance.Currency.API` (SDD-FIN-005)
- A `IWarehouseNomenclatureClient` Refit interface for countries/states/cities
- Aggressive Redis caching of all read endpoints (SDD-INFRA-004)
- Database seeding (ISO 4217 currency list) gated by `EnableCurrencySeeding` feature flag
- Cascading dropdown integration in the React SPA (Country → State → City for counterparty addresses)

**Out of scope:**
- Address-format localization (no formatting rules)
- Postal code validation
- Owning the country / state / city catalogue — that stays in Warehouse Nomenclature
- Tax rates (those are country-strategy concerns, see SDD-CTRY-001)

**Related specs:**
- `SDD-CTRY-BG-001` — Bulgaria strategy seeds `BGN`, `EUR`, `USD` as default visible currencies and configures BNB as the rate provider
- `SDD-INT-WH-002` — Finance → Warehouse Refit client (for borrowing the Warehouse country/state/city catalogue)
- `SDD-INV-001` (Invoices) and `SDD-PAY-001` (Payments) reference currencies on every line and amount

## 2. Behavior

### 2.1 Currency CRUD (MUST)
- `GET /api/v1/currencies` lists all currencies ordered by `IsoCode`. Returns cached payload (`finance-nomenclature:currencies:all`, TTL 30 min).
- `POST /api/v1/currencies` creates a currency (`IsoCode` ISO 4217 alpha-3, `Name`, `Symbol`, `IsActive`). Requires `finance.nomenclature:write`.
- `PUT /api/v1/currencies/{isoCode}` updates `Name`, `Symbol`, `IsActive`. `IsoCode` is immutable.
- `DELETE` is NOT exposed; deactivate via `IsActive = false`.
- Uniqueness on `IsoCode` (3 uppercase letters).

### 2.2 Exchange rates — read (MUST)
- `GET /api/v1/exchange-rates?currency={iso}&date={yyyy-MM-dd}` returns the latest rate on or before the date.
- `GET /api/v1/exchange-rates?currency={iso}&from={d1}&to={d2}` returns the range.
- Caching: never cache transactional reads (rates change daily) — this endpoint MUST hit the database. (Caching the **latest** rate per currency for 5 min is acceptable and tracked separately in SDD-FIN-005.)

### 2.3 Country / State / City — proxied (MUST)
- `GET /api/v1/countries`, `/states?country={iso2}`, `/cities?stateId={id}` proxy to Warehouse Nomenclature via `IWarehouseNomenclatureClient`.
- Results are cached at the Finance gateway for 30 min keyed by query string.

### 2.4 Cascading dropdowns in SPA (MUST)
- The shared `useNomenclature()` React hook (in `frontend/src/shared/hooks/`) exposes `countries`, `getStates(countryIso)`, `getCities(stateId)`, `currencies`. Forms MUST use this hook for counterparty addresses; they MUST NOT hard-code dropdowns.

### 2.5 Database seeding (MUST when enabled)
- On startup, when `EnableCurrencySeeding == true`, the service MUST upsert all currencies from a built-in ISO 4217 list (~180 entries). Existing rows are NOT overwritten.

## 3. Validation Rules

| Field | Rule | Error code |
|---|---|---|
| `IsoCode` | 3 uppercase letters | `INVALID_CURRENCY_CODE` |
| `Name` | NotEmpty, MaxLength 100 | `INVALID_CURRENCY_NAME` |
| `Symbol` | MaxLength 5 | `INVALID_CURRENCY_SYMBOL` |
| Uniqueness | Unique on `IsoCode` | `DUPLICATE_CURRENCY_CODE` |

## 4. Error Rules

| Code | HTTP | Meaning |
|---|---|---|
| `INVALID_CURRENCY_CODE` | 400 | Not 3 uppercase letters |
| `DUPLICATE_CURRENCY_CODE` | 409 | Already exists |
| `CURRENCY_NOT_FOUND` | 404 | No matching row |
| `EXCHANGE_RATE_NOT_FOUND` | 404 | No rate on/before the requested date |
| `WAREHOUSE_NOMENCLATURE_UNREACHABLE` | 503 | Country/state/city proxy down — frontend SHOULD allow free-text fallback for non-critical fields |

Constants live in `Finance.Common.ErrorCodes.NomenclatureErrorCodes`.

## 5. Versioning Notes

v1 ships currency CRUD and rate read. New endpoints (e.g., tax-jurisdiction catalogues) are additive. Removing a currency from the seed list is NOT permitted (historical invoices reference it).

## 6. Test Plan

| Test | Kind |
|---|---|
| `ListCurrencies_ReturnsActiveAndInactive_OrderedByIsoCode` | [Integration] |
| `ListCurrencies_ServedFromRedis_OnSecondCall` | [Integration] |
| `CreateCurrency_PersistsRowAndInvalidatesCache` | [Integration] |
| `CreateCurrency_Returns409_OnDuplicateIso` | [Integration] |
| `UpdateCurrency_DoesNotAllowIsoCodeChange` | [Integration] |
| `Seeder_UpsertsAllIso4217_OnFirstStartup` | [Integration] |
| `Seeder_SkipsExistingRows` | [Integration] |
| `WarehouseProxy_CountriesEndpoint_ReturnsResponseFromUpstream` | [Integration] |
| `WarehouseProxy_ReturnsCachedResponse_OnSecondCall` | [Integration] |
| `WarehouseProxy_Returns503_WhenUpstreamUnreachable` | [Integration] |
| `CurrencyValidator_RejectsLowercaseCode` | [Unit] |

## 7. Open Items

- Whether Finance owns its own country catalogue or always proxies Warehouse. Current choice: proxy. Revisit if Finance ever ships standalone (without Warehouse).
- Multi-language currency `Name` (BG + EN) — defer until SDD-UI shows it's needed.
- Historical exchange rate import (one-off seed from BNB CSV) — separate `CHG-FEAT-*`.
