# SDD-ACCT-001 — Chart of Accounts

> Status: Active
> Owner: Finance
> Related: SDD-INFRA-001 (correlation, ProblemDetails, error-code mapping), SDD-INFRA-004 (Redis cache), SDD-INFRA-005 (filtering/paging), SDD-INFRA-006 (MassTransit outbox + idempotency), SDD-INFRA-007 (validation chain), SDD-INFRA-009 (base service / controller helpers), SDD-AUDIT-001 (immutable audit trail), SDD-OBS-001 (observability), SDD-INT-AUTH-001 (shared JWT auth), SDD-FIN-001 (future), SDD-CTRY-001 (future)
> ISA-95: Level 4 (Business Planning & Logistics) — reference data

---

## 1. Context

The Chart of Accounts is the foundational reference dataset for the entire Finance system. Every journal entry line, invoice line, and posting rule references an `Account` by code. The chart is country-specific (e.g., Bulgaria uses НСС — 304 Стоки, 401 Доставчици, 411 Клиенти, 501 Каса, 503 Банка), so accounts are tagged with a `CountryCode`.

The service supports CRUD and listing for the chart. As of Batch 4 the Accounts service is the first service to fully adopt the always-active cross-cutting foundations: it inherits the shared base service/controller helpers (`SDD-INFRA-009`), exposes a filtered/paged list (`SDD-INFRA-005`), enforces cross-aggregate rules through a validation chain (`SDD-INFRA-007`), publishes domain events through the transactional outbox (`SDD-INFRA-006`), writes immutable audit rows (`SDD-AUDIT-001`), caches reference reads (`SDD-INFRA-004`), and is traced via OpenTelemetry (`SDD-OBS-001`).

**ISA-95 classification.** `Account` is an ISA-95 **Level 4 (Business Planning & Logistics)** reference/master-data entity (ISA-95 / IEC 62264 Part 1, Level 4). Create / update / deactivate are reference-data maintenance operations that emit immutable domain events for state changes (SDD-INFRA-006); they are not business transactions and model no Level 3 (MES) activity.

**Scope — covered:** list (filtered/paged), get-by-id, create, update (Name + IsActive), deactivate (via update), optimistic concurrency, reference-read caching, domain-event publication, audit recording.

**Scope — excluded (deferred):** Seeding from `ICountryStrategy` (BG initial chart) — Phase 2. Posting validation (account referenced-by-journal-entries / `ACCOUNT_HAS_ENTRIES`) and hard delete — Phase 3+. Multi-country selection per request — future spec. Hierarchical reporting roll-ups — Phase 7. Bulk import and account merge/renumber — deferred.

## 2. Behavior

> **Service/controller contract (SDD-INFRA-009).** `AccountService` inherits `SearchableServiceBase<Account, AccountDto, AccountsDbContext>` (and thus `BaseEntityService<AccountsDbContext>`). Every public service method MUST return `Result` / `Result<T>` — it MUST NOT return `null` or throw for business outcomes. `AccountsController` inherits `BaseApiController` and every action MUST translate the service result via `ToActionResult(...)`. Error-code → HTTP-status mapping and the ProblemDetails shape are owned by `SDD-INFRA-001`.

### 2.1 List (MUST)
- `GET /api/v1/accounts` MUST accept a `FilterRequest` from the query string and MUST return a `PagedResult<AccountDto>` envelope produced by `SearchAsync` per `SDD-INFRA-005`. **This is a response-shape change from v1's bare array to a paged envelope — see §5 and the frontend note.**
- The list MUST default-order by `CountryCode` then `Code` ascending. This default ordering MUST be applied by overriding `BuildBaseQuery` so it holds even when the request supplies no sort term; the filtering library always appends the PK as the final deterministic sort term.
- Filterable/sortable/searchable surface MUST be opt-in via attributes on the `Account` entity: `Code` (`[Filterable][Sortable][Searchable]`), `Name` (`[Filterable][Sortable][Searchable]`), `Type` (`[Filterable][Sortable]`), `CountryCode` (`[Filterable][Sortable]`), `IsActive` (`[Filterable][Sortable]`).
- `PageSize` MUST be capped at 200 per `SDD-INFRA-005`.
- The endpoint MUST require permission `finance.account:read`.
- Inactive accounts MUST be included in the listing (no implicit `IsActive` filter); callers exclude them by filtering on `IsActive`.
- The filtered/paged list MUST NOT be cached (it is not reference data — see §2.7).

### 2.2 Get by ID (MUST)
- `GET /api/v1/accounts/{id}` MUST return the account, or a 404 ProblemDetails with `ACCOUNT_NOT_FOUND` when the account does not exist. The service MUST resolve this via `FindOrNotFound` returning `Result<AccountDto>`.
- Requires permission `finance.account:read`.
- The single-account read MAY be served from cache under key `finance-accounts:account:{id}` (`SDD-INFRA-004`).

### 2.3 Create (MUST)
- `POST /api/v1/accounts` MUST create a new account in the chart for the currently configured `Country:Code`.
- Requires permission `finance.account:write`.
- The request body MUST include `Code`, `Name`, `Type`; MAY include `ParentId`.
- Shape validation (`Code`/`Name`/`Type`) MUST run through FluentValidation (`CreateAccountRequestValidator`) referencing constants in `AccountErrorCodes`.
- Cross-aggregate validation MUST run through `IChainValidator<CreateAccountRequest>` (`SDD-INFRA-007`) **before** persisting, and the service MUST return `Result.Failure(code, detail)` on the first failure:
  - `DuplicateAccountCodeValidator` — `Code` MUST be unique within `(CountryCode, Code)`; a duplicate MUST yield `DUPLICATE_ACCOUNT_CODE` (409 Conflict).
  - `ParentAccountValidator` — when `ParentId` is supplied, the parent MUST exist AND belong to the same `CountryCode`; otherwise `INVALID_PARENT_ACCOUNT` (400).
- `Type` MUST be one of `Asset`, `Liability`, `Equity`, `Revenue`, `Expense`.
- `IsActive` MUST be `true` on creation.
- `CreatedAt` MUST be set server-side to `DateTimeOffset.UtcNow` (column default `SYSDATETIMEOFFSET()`).
- On success the service MUST, within a single SaveChanges transaction and in this order: (1) write an audit row, then (2) enqueue the domain event to the outbox (audit-first — see §2.8/§2.9), and (3) invalidate the `finance-accounts:*` cache region.

### 2.4 Update (MUST)
- `PUT /api/v1/accounts/{id}` MUST update `Name` and `IsActive` only.
- Requires permission `finance.account:write`.
- Shape validation MUST run through `UpdateAccountRequestValidator`: `Name` NotEmpty + MaxLength 200; `IsActive` accepted as supplied. Codes reference `AccountErrorCodes`.
- `Code`, `Type`, `ParentId`, `CountryCode` MUST be immutable after creation.
- `UpdatedAt` MUST be set server-side.
- A 404 ProblemDetails with `ACCOUNT_NOT_FOUND` MUST be returned if the account does not exist.
- Optimistic concurrency MUST be enforced: the request carries the base64 `RowVersion` from the prior read; a stale token MUST yield `CONCURRENT_MODIFICATION` (409) via `SaveWithConcurrencyCheck` (`SDD-INFRA-009`). See §2.10.
- An update that sets `IsActive` from `true` to `false` is the deactivate path (§2.5) and MUST publish `AccountDeactivatedEvent` with an audit `StateChange` carrying a `Reason`; an update that only changes `Name` (or reactivates) MUST publish `AccountUpdatedEvent` with an audit `Update`. Both run audit-first, then outbox, then cache invalidation, in the same transaction.

### 2.5 Deactivate (MUST) / Hard delete (MUST NOT)
- Hard delete MUST NOT be exposed. To retire an account, set `IsActive = false` via update (§2.4).
- Deactivation MUST write an audit `StateChange` entry that includes a non-empty `Reason` (`SDD-AUDIT-001`) and MUST publish `AccountDeactivatedEvent` via the outbox.
- The deactivation `Reason` is a **system-supplied standard reason** (`AccountAuditEventTypes.DefaultDeactivationReason`); the audit trail still captures who / when / what. A **caller-supplied** deactivation reason is NOT part of this version — it is a future enhancement (consistent with `SDD-NOM-001` §2.1).
- Future spec extension (Phase 3+) MAY add `DELETE /api/v1/accounts/{id}` with `ACCOUNT_HAS_ENTRIES` (409) guarding any account referenced by a posted journal entry.

### 2.6 Country awareness (MUST)
- The owning country code MUST be derived from the `Country:Code` configuration value on the service. (Multi-country selection per-request is deferred to a future spec.)
- The canonical country-code format MUST be **ISO 3166-1 alpha-2** (e.g. `BG`, `DE`). The validation surface treats it as a 2-character code.
- **Reconciliation note (spec ↔ code).** The persisted `CountryCode` column is provisioned at `MaxLength 3` (see `AccountConfiguration`), one character wider than alpha-2, to leave headroom for non-standard region codes without a schema change. The spec is reconciled to the code: format is authoritative as ISO 3166-1 alpha-2, while the storage width is documented as 3. No code change is required for this batch.

### 2.7 Reference-read caching (MUST — SDD-INFRA-004)
- Only reference reads MUST be cached: get-by-id (`finance-accounts:account:{id}`) and the full active-chart list used for dropdowns (`finance-accounts:chart:all`).
- The filtered/paged `SearchAsync` list endpoint MUST NOT be cached.
- Every create, update, and deactivate MUST invalidate the `finance-accounts:*` region (pattern removal bounded to the `finance-accounts:` prefix).
- Cache access MUST fall through to the database if Redis is unreachable — service availability MUST NOT depend on Redis.

### 2.8 Domain events (MUST — SDD-INFRA-006)
- Create MUST publish `AccountCreatedEvent`; non-deactivating update MUST publish `AccountUpdatedEvent`; deactivation MUST publish `AccountDeactivatedEvent`.
- Events MUST be `sealed record` types implementing `IFinanceEvent` in `src/Finance.ServiceModel/Events/Accounts/`, carrying `MessageId`, `CorrelationId`, `OccurredAt` plus `AccountId`, `Code`, `Name`, `Type`, `CountryCode`, `IsActive`.
- `CorrelationId` MUST be sourced from `ICorrelationIdAccessor`; `MessageId` MUST be a new GUID at construction; `OccurredAt` MUST be `DateTimeOffset.UtcNow`.
- Publication MUST go through the MassTransit EF-Core transactional outbox configured on `AccountsDbContext` (atomic with the DB transaction). The service MUST NOT `await _bus.Publish(...)` outside the outbox and MUST NOT wrap the publish in try/catch.

### 2.9 Audit trail (MUST — SDD-AUDIT-001)
- Create MUST record an audit `Create` entry with `BeforeJson = null`.
- Update MUST record an audit `Update` entry whose `BeforeJson` is the prior-state snapshot.
- Deactivate MUST record an audit `StateChange` entry with a non-empty `Reason`.
- Audit rows MUST be written in the SAME transaction as the change and BEFORE the outbox publish (audit-first). The audit row MUST be persisted via the shared `IAuditService.RecordAsync` into the `audit` schema; the service MUST NOT bypass it.

### 2.10 Optimistic concurrency (MUST)
- `Account` MUST carry a `RowVersion` (`rowversion` / `byte[]`) concurrency token configured via Fluent API.
- `AccountDto` MUST expose `RowVersion` as a base64 string so clients can round-trip it on update.
- A concurrent write detected by `SaveWithConcurrencyCheck` MUST surface as `CONCURRENT_MODIFICATION` (409).

### 2.11 Cross-cutting obligations (MUST)
- Every endpoint MUST be protected by `[RequirePermission("finance.account:<action>")]` and decoded via the shared `Warehouse.Auth.Shared` package (`SDD-INT-AUTH-001`).
- Correlation MUST flow via `ICorrelationIdAccessor` / `CorrelationIdMiddleware`; outbound events MUST carry the ambient `CorrelationId` (`SDD-INFRA-001`).
- The service MUST be traced via OpenTelemetry with the `correlation_id` Activity tag (`SDD-OBS-001`); logging MUST use NLog structured templates with no string interpolation.

## 3. Validation

### 3.1 Shape (FluentValidation)

| Request | Field | Rule | Error code | Validator |
|---|---|---|---|---|
| Create | `Code` | NotEmpty, MaxLength 20 | `INVALID_ACCOUNT_CODE` | `CreateAccountRequestValidator` |
| Create | `Name` | NotEmpty, MaxLength 200 | `INVALID_ACCOUNT_CODE` | `CreateAccountRequestValidator` |
| Create | `Type` | IsInEnum | `INVALID_ACCOUNT_TYPE` | `CreateAccountRequestValidator` |
| Update | `Name` | NotEmpty, MaxLength 200 | `INVALID_ACCOUNT_CODE` | `UpdateAccountRequestValidator` |
| Update | `IsActive` | Accepted as supplied (bool) | — | `UpdateAccountRequestValidator` |

### 3.2 Cross-aggregate (validation chain — SDD-INFRA-007)

| Request | Rule | Error code | Chain validator |
|---|---|---|---|
| Create | `ParentId` (when provided) must exist AND share `CountryCode` | `INVALID_PARENT_ACCOUNT` | `ParentAccountValidator` |
| Create | `Code` unique on `(CountryCode, Code)` | `DUPLICATE_ACCOUNT_CODE` | `DuplicateAccountCodeValidator` |

### 3.3 State-based

| Condition | Rule | Error code |
|---|---|---|
| Update against a stale `RowVersion` | Reject the write | `CONCURRENT_MODIFICATION` |
| `Code`, `Type`, `ParentId`, `CountryCode` on update | Immutable — ignored / rejected, never changed | — |

## 4. Error Rules

All errors emitted as ProblemDetails per `SDD-INFRA-001` (`title` = code in SCREAMING_SNAKE_CASE, `type` = `https://finance.local/errors/{code}`, `detail` = developer English). The error-code → HTTP-status mapping is owned by `DefaultErrorCodeToStatusMap`; the service returns `Result.Failure(code, detail)` and `BaseApiController.ToActionResult` performs the mapping.

| Code | HTTP | Trigger | Type |
|---|---|---|---|
| `INVALID_ACCOUNT_CODE` | 400 | Code/Name missing or too long | Validation (shape) |
| `INVALID_ACCOUNT_TYPE` | 400 | Type not in enum | Validation (shape) |
| `INVALID_PARENT_ACCOUNT` | 400 | Parent does not exist or wrong country | Validation (chain) |
| `DUPLICATE_ACCOUNT_CODE` | 409 | Code already used in this country | Conflict (chain) |
| `CONCURRENT_MODIFICATION` | 409 | Stale `RowVersion` on update | Conflict (concurrency) |
| `ACCOUNT_NOT_FOUND` | 404 | Account does not exist (get / update) | Not found |
| `ACCOUNT_INACTIVE` | 409 | (Future: when posting against inactive) | Conflict |
| `ACCOUNT_HAS_ENTRIES` | 409 | (Future: delete blocked) | Conflict |

Account constants live in `Finance.Common.ErrorCodes.AccountErrorCodes`; `CONCURRENT_MODIFICATION` lives in `Finance.Common.ErrorCodes.CommonErrorCodes`.

> **Resolved decision — `DUPLICATE_ACCOUNT_CODE` status.** v1 mapped this to 400. As of v2 it is enforced through the validation chain and mapped to **409 Conflict**, matching the semantics of a uniqueness clash. Frontend must treat both 400 (shape) and 409 (conflict) responses through `getApiErrorMessage`.

## 5. Versioning

`/api/v1/accounts/*` is the v1 surface.

- **v1 — Initial specification (Phase 0 shell).** CRUD + bare-array list; service returned nullable DTOs; controller returned `Ok`/`NotFound`; `DUPLICATE_ACCOUNT_CODE` → 400; no events/audit/cache/concurrency.
- **v2 — Cross-cutting adoption (Batch 4), non-breaking at the route level / behavior-breaking for the list response shape.**
  - **List response shape changed** from a bare `AccountDto[]` to a `PagedResult<AccountDto>` envelope (`items` + paging metadata) and now accepts a `FilterRequest` (SDD-INFRA-005). This is **breaking for response consumers** — the frontend `AccountsListPage` MUST read `response.items` instead of treating the body as an array, and adopt server-side filter/sort/page. Default order remains `CountryCode` then `Code`.
  - `DUPLICATE_ACCOUNT_CODE` remapped from 400 to **409**, and `INVALID_PARENT_ACCOUNT` enforcement moved into the validation chain (still 400).
  - `AccountDto` gained `RowVersion` (base64) — additive.
  - Service now returns `Result<T>`; controller returns `ToActionResult(...)` (internal, no contract change beyond the list shape).
  - Added `UpdateAccountRequestValidator`; added domain-event publication, audit recording, reference-read caching, optimistic concurrency, and OpenTelemetry tracing.
  - Adding new fields to a response remains additive. Renaming or removing fields requires `/api/v2/`.

## 6. Test Plan

> Environment: Docker/SQL/Redis/RabbitMQ are not available — only unit tests run by default. EF unit tests use SQLite in-memory. Full `WebApplicationFactory` HTTP tests (need auth-service/SQL/Redis/RabbitMQ) MUST be `[Category("Integration")]` and excluded from the default run. All business tests MUST reference `[Category("SDD-ACCT-001")]`.

### 6.1 Service & validation (Unit)

| Test name | Kind |
|---|---|
| `GetAsync_ReturnsNotFoundResult_WhenAccountDoesNotExist` | [Unit] |
| `GetAsync_ReturnsAccount_WhenExists` | [Unit] |
| `SearchAsync_ReturnsPagedResultOrderedByCountryThenCode` | [Unit] |
| `SearchAsync_IncludesInactiveAccounts_WhenNoFilterApplied` | [Unit] |
| `SearchAsync_CapsPageSizeAt200` | [Unit] |
| `CreateAsync_PersistsAccount_WithDefaultIsActiveTrue` | [Unit] |
| `CreateAsync_SetsCountryCodeFromConfiguration` | [Unit] |
| `CreateAsync_ReturnsDuplicateAccountCodeFailure_WhenCodeExistsInSameCountry` | [Unit] |
| `CreateAsync_ReturnsInvalidParentAccountFailure_WhenParentMissingOrWrongCountry` | [Unit] |
| `CreateAsync_RecordsAuditCreate_BeforeOutboxPublish` | [Unit] |
| `CreateAsync_PublishesAccountCreatedEvent_WithCorrelationId` | [Unit] |
| `CreateAsync_InvalidatesFinanceAccountsCacheRegion` | [Unit] |
| `UpdateAsync_ChangesNameAndIsActive_DoesNotChangeImmutableFields` | [Unit] |
| `UpdateAsync_ReturnsNotFoundResult_WhenAccountDoesNotExist` | [Unit] |
| `UpdateAsync_PublishesAccountDeactivatedEvent_AndAuditStateChange_WhenIsActiveSetFalse` | [Unit] |
| `UpdateAsync_PublishesAccountUpdatedEvent_AndAuditUpdate_WhenNameChanged` | [Unit] |
| `UpdateAsync_ReturnsConcurrentModificationFailure_WhenRowVersionStale` | [Unit] |
| `CreateAccountRequestValidator_RejectsEmptyCode` | [Unit] |
| `CreateAccountRequestValidator_RejectsInvalidType` | [Unit] |
| `UpdateAccountRequestValidator_RejectsEmptyName` | [Unit] |
| `DuplicateAccountCodeValidator_FailsWithDuplicateAccountCode_WhenCodePresentInCountry` | [Unit] |
| `ParentAccountValidator_FailsWithInvalidParentAccount_WhenParentInDifferentCountry` | [Unit] |
| `AccountConfiguration_HasUniqueIndexOnCountryAndCode` | [Unit] |
| `AccountConfiguration_ConfiguresRowVersionConcurrencyToken` | [Unit] |

### 6.2 Endpoint & wiring (Integration — `[Category("Integration")]`, excluded from default run)

| Test name | Kind |
|---|---|
| `List_ReturnsEmptyPagedResult_WhenNoAccounts` | [Integration] |
| `List_ReturnsPagedResultOrderedByCountryAndCode` | [Integration] |
| `List_AppliesFilterAndSortFromQueryString` | [Integration] |
| `Get_Returns404ProblemDetails_WhenAccountDoesNotExist` | [Integration] |
| `Get_ReturnsAccount_WhenExists` | [Integration] |
| `Create_Returns201_AndPersistsAccount` | [Integration] |
| `Create_Returns400ProblemDetails_WhenCodeMissing` | [Integration] |
| `Create_Returns400ProblemDetails_WhenTypeInvalid` | [Integration] |
| `Create_Returns409ProblemDetails_WhenDuplicateCodeInSameCountry` | [Integration] |
| `Create_Returns400ProblemDetails_WhenParentDoesNotExist` | [Integration] |
| `Create_WritesOutboxMessageAndAuditRow_InSameTransaction` | [Integration] |
| `Update_ChangesNameAndIsActive_DoesNotChangeImmutableFields` | [Integration] |
| `Update_Returns404ProblemDetails_WhenAccountDoesNotExist` | [Integration] |
| `Update_Returns409ProblemDetails_WhenRowVersionStale` | [Integration] |
| `Endpoint_Returns403_WhenPermissionMissing` | [Integration] |

## 7. Open Items

- Country chart seed (БГ НСС: 100s equity, 200s long-term, 300s materials/inventory, 400s receivables/payables, 500s monetary, 600s expenses, 700s revenue) — Phase 2 via `ICountryStrategy.GetDefaultChartOfAccounts()`.
- Hierarchical reporting (account roll-ups by parent for Balance Sheet / Income Statement) — Phase 7 reporting service.
- Bulk import (CSV / XLSX) — deferred.
- Account merge / renumber — deferred; financial regulators typically forbid this once entries exist.
- `audit.OperationsEvents` INSERT-only DB grant + nightly tamper verification — tracked under `SDD-AUDIT-001` (deferred per that spec).
- Frontend `AccountsListPage` migration to the `PagedResult<AccountDto>` envelope + server-side filter/sort/page (`SDD-INFRA-005`, `SDD-UI-001`).
