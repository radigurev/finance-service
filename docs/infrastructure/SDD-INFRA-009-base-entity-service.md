# SDD-INFRA-009 — Base Entity Service & Common Service Helpers

> Status: Planned
> Owner: Platform
> Last updated: 2026-05-28
> Category: Infrastructure
> Related: SDD-INFRA-001, SDD-INFRA-005, SDD-ACCT-001
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
- `SaveWithConcurrencyCheckAsync` MUST translate `DbUpdateConcurrencyException` to `Result.Failure(CONCURRENT_MODIFICATION)` (SDD-INFRA-008).

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
- Already in `Finance.Common` (Phase 0 placeholder). v1 surface:
```csharp
public sealed record Result<T>(bool IsSuccess, T? Value, string? ErrorCode, string? Detail)
{
    public static Result<T> Success(T value) => new(true, value, null, null);
    public static Result<T> Failure(string code, string? detail = null) => new(false, default, code, detail);
}
```
- Services MUST return `Result<T>` from public methods.
- Controllers MUST consume `Result<T>` via `BaseApiController.ToActionResult`.

## 3. Validation Rules

- A service that returns `Result.Success(null)` for a reference type MUST be considered a bug — failures must use `Result.Failure(...)`.
- Subclasses of `SearchableServiceBase` MUST register `[Filterable]` / `[Sortable]` attributes on `TEntity` properties they want exposed (SDD-INFRA-005).

## 4. Error Rules

This spec defines the mapping, not the codes. Codes themselves live in domain SDDs (`AccountErrorCodes`, `InvoiceErrorCodes`, …) and `WorkflowErrorCodes` / `FilterErrorCodes` for cross-cutting concerns.

## 5. Versioning Notes

v1 is the surface above. Adding a helper method is additive. Changing the signature of an existing helper is a breaking change and requires bumping the `Finance.Infrastructure` package major version.

## 6. Test Plan

| Test | Kind |
|---|---|
| `FindOrNotFoundAsync_ReturnsNotFoundFailure_WhenMissing` | [Unit] |
| `FindOrNotFoundAsync_ReturnsEntity_WhenPresent` | [Unit] |
| `MapAndSaveAsync_PersistsAndReturnsDto` | [Integration] |
| `SaveWithConcurrencyCheckAsync_TranslatesDbUpdateConcurrencyException` | [Integration] |
| `SearchAsync_AppliesFilterPaginationAndProjection` | [Integration] |
| `SearchAsync_RespectsBaseQueryOverride` | [Unit] |
| `PrimaryFlagHelper_FlagsFirst_WhenNoneFlagged` | [Unit] |
| `PrimaryFlagHelper_KeepsOnlyFirstPrimary_WhenMultipleFlagged` | [Unit] |
| `PrimaryFlagHelper_IsNoOp_OnEmptyList` | [Unit] |
| `ToActionResult_MapsNotFoundCodeTo404` | [Unit] |
| `ToActionResult_BuildsProblemDetailsWithTitleAndType` | [Unit] |

## 7. Open Items

- `SaveWithConcurrencyCheck` retry policy (auto-reload + reapply vs surface to client). Today: surface to client (simpler, safer). Auto-reload is in scope only when a non-conflicting fix is obvious — defer.
- A "soft-delete" base helper. Finance generally forbids soft-delete on transactional rows (use reversal); but reference data (accounts, currencies) uses `IsActive`. A small helper for the active-row filter would tighten the convention — track as `CHG-ENH-*`.
