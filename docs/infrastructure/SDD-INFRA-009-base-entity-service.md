# SDD-INFRA-009 — Base Entity Service & Common Service Helpers

> Status: Active (Batch 1 — canonical `Result` / `Result<T>` outcome types in `Finance.Common/Results` shipping; `BaseEntityService`, `SearchableServiceBase`, `PrimaryFlagHelper`, `BaseApiController` deferred to Batch 2 in `Finance.Infrastructure.Services` — they need EF Core / ASP.NET)
> Owner: Platform
> Last updated: 2026-05-30
> Category: Infrastructure
> Related: SDD-INFRA-001, SDD-INFRA-005, SDD-INFRA-007, SDD-INFRA-008, SDD-ACCT-001
> Mirrors: Warehouse `Warehouse.Infrastructure.Services` (`BaseEntityService`, `PrimaryFlagHelper`, `SearchableServiceBase`)

---

## 1. Context & Scope

This spec defines `Finance.Infrastructure.Services`, a small set of base classes and helpers that every Finance service inherits to remove boilerplate around find-or-404, map-and-save, primary-flag enforcement, and searchable / paginated list endpoints. The shape is identical to Warehouse's `BaseEntityService` so anyone moving between codebases sees the same conventions.

**In scope:**
- `BaseEntityService<TContext>` — generic helpers for `FindOrNotFound`, `SaveWithConcurrencyCheck`, `MapAndSave`
- `SearchableServiceBase<TEntity, TDto>` — generic list endpoint backed by `SDD-INFRA-005 Generic Filtering`
- `PrimaryFlagHelper` — manages "exactly one primary" semantics on collections (e.g., a counterparty's primary email)
- `BaseApiController` — `Result<T> → ActionResult<T>` translation, ProblemDetails wiring, version-aware route builder
- `Result` / `Result<T>` outcome types (already in `Finance.Common`; this spec describes how they're consumed)

**Out of scope:**
- Repository pattern — this is intentionally NOT a repository base. EF Core `DbSet<T>` is the repository.
- CQRS handlers — kept out of Finance per user decision (service/repository pattern only).
- Domain-specific service logic — those live in each microservice.

### Resolved Decision — what ships in Batch 1 vs Batch 2
- **Batch 1 (`src/Finance.Common/Results/`) — the canonical outcome types only (no EF Core / ASP.NET dependency):**
  - `sealed record Result(bool IsSuccess, string? ErrorCode, string? Detail)` with static `Result Success()` and `Result Failure(string code, string? detail = null)`.
  - `sealed record Result<T>(bool IsSuccess, T? Value, string? ErrorCode, string? Detail)` with static `Result<T> Success(T value)` and `Result<T> Failure(string code, string? detail = null)`.
  - These are the canonical outcome types returned by **every** Finance service and consumed by `IWorkflowEngine` (SDD-INFRA-008) and the validation chain integration (SDD-INFRA-007). Unit tests live in `src/Finance.Common.Tests`.
- **Batch 2 (`src/Finance.Infrastructure.Services/`) — the base classes and helpers, which need EF Core / ASP.NET:**
  - `BaseEntityService<TContext>`, `SearchableServiceBase<TEntity, TDto>`, `PrimaryFlagHelper`, `BaseApiController`, and the `IErrorCodeToStatusMap` HTTP mapping.

## 2. Behavior

### 2.1 `BaseEntityService<TContext>` (MUST)
```csharp
public abstract class BaseEntityService<TContext> where TContext : DbContext
{
    protected TContext Db { get; }
    protected IMapper Mapper { get; }
    protected ICorrelationIdAccessor Correlation { get; }

    protected async Task<Result<TEntity>> FindOrNotFoundAsync<TEntity>(
        int id, string notFoundErrorCode, CancellationToken ct) where TEntity : class;

    protected async Task<Result<TDto>> MapAndSaveAsync<TEntity, TDto>(
        TEntity entity, CancellationToken ct) where TEntity : class;

    protected async Task<Result> SaveWithConcurrencyCheckAsync(CancellationToken ct);
}
```
- `FindOrNotFoundAsync<TEntity>` MUST return `Result.Failure(notFoundErrorCode)` when the entity is missing. Callers MUST supply a domain-specific code (`ACCOUNT_NOT_FOUND`, `INVOICE_NOT_FOUND`, …).
- `MapAndSaveAsync<TEntity, TDto>` MUST add the entity to the context, call `SaveChangesAsync`, then map to `TDto`.
- `SaveWithConcurrencyCheckAsync` MUST translate `DbUpdateConcurrencyException` to `Result.Failure(CommonErrorCodes.CONCURRENT_MODIFICATION)` (single source in `CommonErrorCodes`; see SDD-INFRA-008). This helper ships in Batch 2 with the rest of `BaseEntityService<TContext>`.

### 2.2 `SearchableServiceBase<TEntity, TDto>` (MUST)
```csharp
public abstract class SearchableServiceBase<TEntity, TDto> : BaseEntityService<TContext> where TEntity : class
{
    public async Task<Result<PagedResult<TDto>>> SearchAsync(
        FilterRequest request,
        CancellationToken ct);
}
```
- The base method MUST apply `request` to `Db.Set<TEntity>().AsNoTracking().ApplyFilter(request)` from SDD-INFRA-005.
- The base method MUST count BEFORE paginating, then project to `TDto` via AutoMapper.
- Subclasses MAY override `BuildBaseQuery()` to add scope (e.g., "only this country", "only active").

### 2.3 `PrimaryFlagHelper` (MUST)
For collections like `CounterpartyEmail[]` where exactly one must be `IsPrimary == true`:
```csharp
public static class PrimaryFlagHelper
{
    public static void EnsureSinglePrimary<T>(IList<T> items, Func<T, bool> getIsPrimary, Action<T, bool> setIsPrimary);
}
```
- If zero items are flagged, the FIRST item MUST be flagged.
- If multiple are flagged, only the FIRST flagged item MUST remain `true`; the rest MUST be set to `false`.
- If the collection is empty, the helper MUST be a no-op.

### 2.4 `BaseApiController` (MUST)
```csharp
[ApiController]
public abstract class BaseApiController : ControllerBase
{
    protected ActionResult<T> ToActionResult<T>(Result<T> result);
    protected ActionResult ToActionResult(Result result);
}
```
- `ToActionResult` MUST map error codes to HTTP statuses via a registered `IErrorCodeToStatusMap` (default: codes ending in `_NOT_FOUND` → 404, `_INACTIVE` / `_DUPLICATE_*` / `_CONFLICT` / `CONCURRENT_*` → 409, `_FORBIDDEN` / `INSUFFICIENT_*` → 403, anything else → 400).
- `ToActionResult` MUST construct ProblemDetails with `title = errorCode`, `detail = result.Detail ?? <fallback>`, `type = https://finance.local/errors/{code}`.
- Validation results (FluentValidation) MUST go through `CustomProblemDetailsFactory` which puts codes in `errors` per SDD-INFRA-001.

### 2.5 `Result` / `Result<T>` (MUST)
- **Resolved Decision (Batch 1):** the canonical outcome types live in `src/Finance.Common/Results/` (one type per file). v1 surface:
```csharp
public sealed record Result(bool IsSuccess, string? ErrorCode, string? Detail)
{
    public static Result Success() => new(true, null, null);
    public static Result Failure(string code, string? detail = null) => new(false, code, detail);
}

public sealed record Result<T>(bool IsSuccess, T? Value, string? ErrorCode, string? Detail)
{
    public static Result<T> Success(T value) => new(true, value, null, null);
    public static Result<T> Failure(string code, string? detail = null) => new(false, default, code, detail);
}
```
- Both `Result` and `Result<T>` ship in Batch 1 and are the canonical outcome types used by every service, by `IWorkflowEngine` (SDD-INFRA-008), and by the validation chain integration (SDD-INFRA-007). Services MUST return `Result` / `Result<T>` from public methods — they MUST NOT throw for business failures.
- Controllers MUST consume `Result` / `Result<T>` via `BaseApiController.ToActionResult` (the consuming controller base ships in Batch 2).

## 3. Validation Rules

- A service that returns `Result.Success(null)` for a reference type MUST be considered a bug — failures must use `Result.Failure(...)`.
- Subclasses of `SearchableServiceBase` MUST register `[Filterable]` / `[Sortable]` attributes on `TEntity` properties they want exposed (SDD-INFRA-005).

## 4. Error Rules

This spec defines the mapping, not the codes. All error-code constants live in `src/Finance.Common/ErrorCodes/`, one class per file, each constant a `public const string` whose value equals its own name (SCREAMING_SNAKE_CASE).

**Resolved Decision — error-code class inventory (Batch 1):**

| Class | Constants |
|---|---|
| `AccountErrorCodes` (exists) | domain codes for Chart of Accounts |
| `CommonErrorCodes` | `GENERIC_ERROR`, `VALIDATION_FAILED`, `CONCURRENT_MODIFICATION` (single source) |
| `AuthErrorCodes` | `MISSING_TOKEN`, `INVALID_TOKEN`, `INSUFFICIENT_PERMISSIONS` |
| `FilterErrorCodes` | `INVALID_FILTER_FIELD`, `INVALID_SORT_FIELD`, `INVALID_OPERATOR`, `INVALID_FILTER_VALUE`, `PAGE_SIZE_TOO_LARGE` |
| `SequenceErrorCodes` | `UNKNOWN_SEQUENCE_KEY`, `SEQUENCE_GAP_DETECTED` |
| `CachingErrorCodes` | `REDIS_UNREACHABLE`, `CACHE_KEY_PATTERN_VIOLATION` |
| `WorkflowErrorCodes` | `INVALID_STATE_TRANSITION`, `WORKFLOW_GUARD_FAILED`, `STATE_NOT_REGISTERED` (`CONCURRENT_MODIFICATION` is referenced from `CommonErrorCodes`, not redefined) |
| `AuditErrorCodes` | `AUDIT_REASON_REQUIRED`, `AUDIT_TAMPERING_DETECTED` |
| `EventLogErrorCodes` | `INVALID_DATE_RANGE`, `EVENT_NOT_FOUND` (reuses `FilterErrorCodes.PAGE_SIZE_TOO_LARGE`) |
| `NomenclatureErrorCodes` | `INVALID_CURRENCY_CODE`, `DUPLICATE_CURRENCY_CODE`, `CURRENCY_NOT_FOUND`, `WAREHOUSE_NOMENCLATURE_UNREACHABLE` |

Domain-specific codes (`InvoiceErrorCodes`, …) are added by their owning microservice phases. The `IErrorCodeToStatusMap` (§2.4) maps any of these codes to an HTTP status in the Batch-2 web layer.

## 5. Versioning Notes

v1 is the surface above. Adding a helper method is additive. Changing the signature of an existing helper is a breaking change and requires bumping the `Finance.Infrastructure` package major version.

## 6. Test Plan

Tests are scheduled against the batch that ships the code they exercise. Integration tests need a real DB and are `[Category("Integration")]` (excluded from the default Batch-1 run — no SQL Server in this environment).

**Batch 1 — `Result` / `Result<T>` (`src/Finance.Common.Tests`):**

| Test | Kind | Batch |
|---|---|---|
| `Result_Success_HasIsSuccessTrueAndNullErrorCode` | [Unit] | Batch 1 |
| `Result_Failure_CarriesErrorCodeAndOptionalDetail` | [Unit] | Batch 1 |
| `ResultOfT_Success_CarriesValue_AndIsSuccessTrue` | [Unit] | Batch 1 |
| `ResultOfT_Failure_HasDefaultValue_AndCarriesErrorCode` | [Unit] | Batch 1 |

**Batch 2 — base classes / helpers / controller (`src/Finance.Infrastructure.Services.Tests`, created in Batch 2):**

| Test | Kind | Batch |
|---|---|---|
| `FindOrNotFoundAsync_ReturnsNotFoundFailure_WhenMissing` | [Unit] | Batch 2 |
| `FindOrNotFoundAsync_ReturnsEntity_WhenPresent` | [Unit] | Batch 2 |
| `MapAndSaveAsync_PersistsAndReturnsDto` | [Integration] | Batch 2 |
| `SaveWithConcurrencyCheckAsync_TranslatesDbUpdateConcurrencyException` | [Integration] | Batch 2 |
| `SearchAsync_AppliesFilterPaginationAndProjection` | [Integration] | Batch 2 |
| `SearchAsync_RespectsBaseQueryOverride` | [Unit] | Batch 2 |
| `PrimaryFlagHelper_FlagsFirst_WhenNoneFlagged` | [Unit] | Batch 2 |
| `PrimaryFlagHelper_KeepsOnlyFirstPrimary_WhenMultipleFlagged` | [Unit] | Batch 2 |
| `PrimaryFlagHelper_IsNoOp_OnEmptyList` | [Unit] | Batch 2 |
| `ToActionResult_MapsNotFoundCodeTo404` | [Unit] | Batch 2 |
| `ToActionResult_BuildsProblemDetailsWithTitleAndType` | [Unit] | Batch 2 |

## 7. Resolved Decisions & Deferred Items

### Resolved (Batch 1)
- **`Result` / `Result<T>` location:** canonical outcome types live in `src/Finance.Common/Results/` (both the non-generic `Result` and generic `Result<T>`) — see §2.5.
- **`CONCURRENT_MODIFICATION` ownership:** single source in `CommonErrorCodes`; referenced (not redefined) by `SaveWithConcurrencyCheckAsync` and the workflow engine.
- **Batch split:** only the outcome types ship in Batch 1; `BaseEntityService`, `SearchableServiceBase`, `PrimaryFlagHelper`, and `BaseApiController` ship in Batch 2 (they need EF Core / ASP.NET) — see §1 batch-split decision.

### Deferred
- `SaveWithConcurrencyCheck` retry policy (auto-reload + reapply vs surface to client). Today: surface to client (simpler, safer). Auto-reload is in scope only when a non-conflicting fix is obvious — defer.
- A "soft-delete" base helper. Finance generally forbids soft-delete on transactional rows (use reversal); but reference data (accounts, currencies) uses `IsActive`. A small helper for the active-row filter would tighten the convention — track as `CHG-ENH-*`.
