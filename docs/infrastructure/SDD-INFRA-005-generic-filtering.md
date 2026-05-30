# SDD-INFRA-005 — Generic Filtering

> Status: Active (Batch 1 — `Finance.GenericFiltering` library + unit tests shipping; real-SQL-Server integration test deferred)
> Owner: Platform
> Last updated: 2026-05-30
> Category: Infrastructure
> Related: SDD-INFRA-001, SDD-INFRA-009, all list endpoints
> Mirrors: Warehouse `Warehouse.GenericFiltering`

---

## 1. Context & Scope

This spec defines `Finance.GenericFiltering`, a reusable library for translating client-supplied filter / sort / paginate parameters into `IQueryable<T>` expressions over EF Core entities. It is the same shape as `Warehouse.GenericFiltering` so that frontend filter components, query-string conventions, and OpenAPI examples can be shared 1:1.

**In scope:**
- `FilterRequest` model: `Filters` (`List<FilterCriterion>`), `Sort` (`List<SortCriterion>`), `Page` (default 1), `PageSize` (default 50), `Search` (`string?`)
- `FilterCriterion(Field, Operator, Value)` and `SortCriterion(Field, Direction)` where `Direction` is `asc` / `desc`
- `PagedResult<T>` response envelope: `Items` (`IReadOnlyList<T>`), `TotalCount`, `Page`, `PageSize`
- `[Filterable]` attribute on entity properties that opts them into client-facing filtering
- `[Sortable]` attribute that opts properties into client-facing sorting
- `[Searchable]` attribute that opts string properties into the OR-LIKE `search` clause
- Operator set: `eq`, `neq`, `gt`, `gte`, `lt`, `lte`, `contains`, `startsWith`, `endsWith`, `in`, `between`, `isNull`, `isNotNull`
- Type-safe expression building for `string`, `int`, `long`, `decimal`, `DateTimeOffset`, `Guid`, `bool`, `enum`
- `IQueryable<T>.ApplyFilter(request)` extension that composes Where + OrderBy + Skip + Take
- Stable error responses (ProblemDetails) for unknown / non-filterable properties or invalid operators

**Out of scope:**
- Server-side aggregation (count/sum/avg) — separate spec
- Full-text search backends (Elastic, Meilisearch) — `Search` is a simple `LIKE '%...%'` ORed across `[Searchable]` properties
- Field-level RBAC (filtering does NOT bypass authorization; permission is checked at the endpoint)

### Resolved Decision — implementation & contract location (Batch 1)
- The library is `src/Finance.GenericFiltering/Finance.GenericFiltering.csproj`, referencing `Finance.Common`. `Directory.Build.props` supplies `TargetFramework net8.0` — the project MUST NOT set it locally.
- `FilterRequest`, `FilterCriterion`, `SortCriterion`, `PagedResult<T>`, the `[Filterable]` / `[Sortable]` / `[Searchable]` attributes, and the `ApplyFilter` extension all live in **this project** — it is the canonical filtering request/response contract. `PagedResult<T>` is consumed by `SearchableServiceBase<TEntity, TDto>` (SDD-INFRA-009).
- The library MUST signal a rejected request by throwing `FilterValidationException(string ErrorCode, string Detail)` carrying the matching `FilterErrorCodes` constant. The Batch-2 web/middleware layer maps the exception to a `400 ProblemDetails` per SDD-INFRA-001 (the pure library has no ASP.NET dependency).
- Unit tests live in `src/Finance.GenericFiltering.Tests` (NUnit). The library ships with EF Core in-memory + LINQ-to-Objects unit tests in Batch 1; the real-SQL-Server `IS NULL` translation test is `[Category("Integration")]` and excluded from the default Batch-1 run (no SQL Server in this environment).

## 2. Behavior

### 2.1 Filter request shape (MUST)
```json
{
  "filters": [
    { "field": "isActive", "operator": "eq", "value": true },
    { "field": "type", "operator": "in", "value": ["Asset", "Liability"] },
    { "field": "createdAt", "operator": "between", "value": ["2026-01-01", "2026-12-31"] }
  ],
  "sort": [
    { "field": "code", "direction": "asc" }
  ],
  "page": 1,
  "pageSize": 50,
  "search": "ДДС"
}
```

### 2.2 Property opt-in (MUST)
- Only properties marked with `[Filterable]` MAY appear in `filters`. Others MUST return `400 INVALID_FILTER_FIELD`.
- Only properties marked with `[Sortable]` MAY appear in `sort`. Others MUST return `400 INVALID_SORT_FIELD`.
- `[Searchable]` on string properties opts them into the OR-LIKE `search` clause.

### 2.3 Operator validity (MUST)
- Operator MUST be valid for the property's CLR type (e.g., `contains` is invalid on `decimal`).
- Invalid operators return `400 INVALID_OPERATOR`.

### 2.4 Pagination (MUST)
- `page` defaults to 1; `pageSize` defaults to 50; max `pageSize` is 200. Larger values return `400 PAGE_SIZE_TOO_LARGE`.
- Response envelope:
  ```json
  { "items": [...], "totalCount": 1234, "page": 1, "pageSize": 50 }
  ```

### 2.5 Authoritative ordering (MUST)
- The library MUST always append a deterministic final sort term so pagination is stable when the client-supplied sort produces ties.
- **Resolved Decision (Batch 1):** the final term is the property named `Id` (discovered via reflection). If the entity has no `Id` property, the library MUST fall back to the first `[Sortable]` property declared on the entity.

### 2.6 Null safety (MUST)
- `isNull` / `isNotNull` operators MUST work on nullable value types and reference types.
- `eq` against `null` is supported but MUST be translated to `IS NULL` in SQL (EF Core handles this; the library MUST NOT rewrite to `= NULL`).

## 3. Validation Rules

| Failure | Code |
|---|---|
| Property not on entity | `INVALID_FILTER_FIELD` |
| Property not marked `[Filterable]` | `INVALID_FILTER_FIELD` |
| Operator not valid for type | `INVALID_OPERATOR` |
| Value cannot be parsed to property type | `INVALID_FILTER_VALUE` |
| `pageSize > 200` | `PAGE_SIZE_TOO_LARGE` |
| `between` value not a 2-element array | `INVALID_FILTER_VALUE` |
| `in` value not an array | `INVALID_FILTER_VALUE` |

All validation errors return HTTP 400 ProblemDetails per SDD-INFRA-001.

## 4. Error Rules

Constants live in `src/Finance.Common/ErrorCodes/FilterErrorCodes.cs`: `INVALID_FILTER_FIELD`, `INVALID_SORT_FIELD`, `INVALID_OPERATOR`, `INVALID_FILTER_VALUE`, `PAGE_SIZE_TOO_LARGE`. Each is a `public const string` whose value equals its own name (SCREAMING_SNAKE_CASE). `PAGE_SIZE_TOO_LARGE` is owned here and **reused** by `SDD-EVTLOG-001` rather than redefined.

The library raises these as `FilterValidationException(ErrorCode, Detail)`; the Batch-2 web layer maps each to a `400 ProblemDetails` (`title` = the code, `type` = `https://finance.local/errors/{code}`) per SDD-INFRA-001.

## 5. Versioning Notes

v1: the operator set in §1. New operators are additive (no version bump). Removing an operator requires `CHG-ENH-*`.

## 6. Test Plan

| Test | Kind |
|---|---|
| `ApplyFilter_TranslatesEqOperatorCorrectly` | [Unit, in-memory EF Core] |
| `ApplyFilter_TranslatesBetweenOperatorCorrectly` | [Unit] |
| `ApplyFilter_TranslatesInOperatorCorrectly` | [Unit] |
| `ApplyFilter_AppliesSortAndPaging` | [Unit] |
| `ApplyFilter_AlwaysAppendsPkAsFinalSort` | [Unit] |
| `ApplyFilter_RejectsNonFilterableField` | [Unit] |
| `ApplyFilter_RejectsInvalidOperator` | [Unit] |
| `ApplyFilter_RejectsPageSizeOver200` | [Unit] |
| `ApplyFilter_SearchOrsAcrossAllSearchableProperties` | [Unit] |
| `ApplyFilter_ProducesIsNullSql_OnEqNull` | [Integration, real SQL Server] |

## 7. Resolved Decisions & Deferred Items

### Resolved (Batch 1)
- **Filter composition:** v1 is **AND-only** — all entries in `filters` are combined with logical AND. Boolean `OR` / grouped composition (structured `groups` model) is deferred to a future `CHG-ENH-*`.
- **Date format:** date values (for `eq`, `between`, comparison operators on `DateTimeOffset`) MUST be supplied as ISO-8601 strings.
- **Enum matching:** enum-typed filter values are matched **by name** (e.g., `AccountType.Asset`), not by ordinal. Ordinal matching is not supported in v1.

### Deferred
- Boolean `OR` / grouped (`groups`) composition — future `CHG-ENH-*`.
- Server-side aggregation (count/sum/avg) — separate spec.
