# SDD-ACCT-001 — Chart of Accounts

> Status: Draft
> Owner: Finance
> Related: SDD-INFRA-001, SDD-INT-AUTH-001, SDD-FIN-001 (future), SDD-CTRY-001 (future)
> ISA-95: Level 4 (Business Planning & Logistics) — reference data

---

## 1. Context

The Chart of Accounts is the foundational reference dataset for the entire Finance system. Every journal entry line, invoice line, and posting rule references an `Account` by code. The chart is country-specific (e.g., Bulgaria uses НСС — 304 Стоки, 401 Доставчици, 411 Клиенти, 501 Каса, 503 Банка), so accounts are tagged with a `CountryCode`.

For the Phase 0 shell, the service supports CRUD and listing only. Seeding from `ICountryStrategy` (BG initial chart) is deferred to Phase 2. Posting validation (referenced-by-journal-entries) is deferred to Phase 3.

## 2. Behavior

### 2.1 List (MUST)
- `GET /api/v1/accounts` MUST return all accounts ordered by `CountryCode` then `Code` ascending.
- The endpoint MUST require permission `finance.account:read`.
- Inactive accounts MUST be included in the listing.

### 2.2 Get by ID (MUST)
- `GET /api/v1/accounts/{id}` MUST return the account or 404 with `ACCOUNT_NOT_FOUND`.
- Requires permission `finance.account:read`.

### 2.3 Create (MUST)
- `POST /api/v1/accounts` MUST create a new account in the chart for the currently configured `Country:Code`.
- Requires permission `finance.account:write`.
- The request body MUST include `Code`, `Name`, `Type`; MAY include `ParentId`.
- `Code` MUST be unique within `(CountryCode, Code)`. Duplicates MUST return 400 with `DUPLICATE_ACCOUNT_CODE`.
- `Type` MUST be one of `Asset`, `Liability`, `Equity`, `Revenue`, `Expense`.
- If `ParentId` is provided, the parent MUST exist and MUST belong to the same `CountryCode`. Otherwise 400 with `INVALID_PARENT_ACCOUNT`.
- `IsActive` is `true` on creation.
- `CreatedAt` MUST be set server-side to `DateTimeOffset.UtcNow`.

### 2.4 Update (MUST)
- `PUT /api/v1/accounts/{id}` MUST update `Name` and `IsActive` only.
- Requires permission `finance.account:write`.
- `Code`, `Type`, `ParentId`, `CountryCode` are immutable after creation.
- `UpdatedAt` MUST be set server-side.
- 404 with `ACCOUNT_NOT_FOUND` if the account does not exist.

### 2.5 Delete (MUST NOT — Phase 0)
- Hard delete MUST NOT be exposed. To retire an account, set `IsActive = false` via update.
- Future spec extension (Phase 3+) MAY add `DELETE /api/v1/accounts/{id}` with `ACCOUNT_HAS_ENTRIES` (409) guarding any account referenced by a posted journal entry.

### 2.6 Country awareness (MUST)
- The owning country code MUST be derived from the `Country:Code` configuration value on the service. (Multi-country selection per-request is deferred to a future spec.)
- Country code MUST be ISO 3166-1 alpha-2 (`BG`, `DE`, `EN`).

## 3. Validation

| Field | Rule | Error code |
|---|---|---|
| `Code` | NotEmpty, MaxLength 20 | `INVALID_ACCOUNT_CODE` |
| `Name` | NotEmpty, MaxLength 200 | `INVALID_ACCOUNT_CODE` |
| `Type` | IsInEnum | `INVALID_ACCOUNT_TYPE` |
| `ParentId` | Must exist in same country (if provided) | `INVALID_PARENT_ACCOUNT` |
| Uniqueness | Unique on `(CountryCode, Code)` | `DUPLICATE_ACCOUNT_CODE` |

## 4. Error Rules

All errors emitted as ProblemDetails per SDD-INFRA-001.

| Code | HTTP | Meaning |
|---|---|---|
| `INVALID_ACCOUNT_CODE` | 400 | Code missing or too long |
| `INVALID_ACCOUNT_TYPE` | 400 | Type not in enum |
| `INVALID_PARENT_ACCOUNT` | 400 | Parent does not exist or wrong country |
| `DUPLICATE_ACCOUNT_CODE` | 400 | Code already used in this country |
| `ACCOUNT_NOT_FOUND` | 404 | Account does not exist |
| `ACCOUNT_INACTIVE` | 409 | (Future: when posting against inactive) |
| `ACCOUNT_HAS_ENTRIES` | 409 | (Future: delete blocked) |

Constants live in `Finance.Common.ErrorCodes.AccountErrorCodes`.

## 5. Versioning

`/api/v1/accounts/*` is the v1 surface. Adding new fields to the response is additive. Renaming or removing fields requires `/api/v2/`.

## 6. Test Plan

| Test name | Kind |
|---|---|
| `List_ReturnsEmptyArray_WhenNoAccounts` | [Integration] |
| `List_ReturnsAccountsOrderedByCountryAndCode` | [Integration] |
| `Get_Returns404_WhenAccountDoesNotExist` | [Integration] |
| `Get_ReturnsAccount_WhenExists` | [Integration] |
| `Create_PersistsAccount_WithDefaultIsActiveTrue` | [Integration] |
| `Create_SetsCountryCodeFromConfiguration` | [Integration] |
| `Create_Returns400_WhenCodeMissing` | [Integration] |
| `Create_Returns400_WhenTypeInvalid` | [Integration] |
| `Create_Returns400_WhenDuplicateCodeInSameCountry` | [Integration] |
| `Create_Returns400_WhenParentDoesNotExist` | [Integration] |
| `Update_ChangesNameAndIsActive_DoesNotChangeImmutableFields` | [Integration] |
| `Update_Returns404_WhenAccountDoesNotExist` | [Integration] |
| `Endpoint_Returns403_WhenPermissionMissing` | [Integration] |
| `CreateAccountRequestValidator_RejectsEmptyCode` | [Unit] |
| `CreateAccountRequestValidator_RejectsInvalidType` | [Unit] |
| `AccountConfiguration_HasUniqueIndexOnCountryAndCode` | [Unit] |

## 7. Open Items

- Country chart seed (БГ НСС: 100s equity, 200s long-term, 300s materials/inventory, 400s receivables/payables, 500s monetary, 600s expenses, 700s revenue) — Phase 2 via `ICountryStrategy.GetDefaultChartOfAccounts()`.
- Hierarchical reporting (account roll-ups by parent for Balance Sheet / Income Statement) — Phase 7 reporting service.
- Bulk import (CSV / XLSX) — deferred.
- Account merge / renumber — deferred; financial regulators typically forbid this once entries exist.
