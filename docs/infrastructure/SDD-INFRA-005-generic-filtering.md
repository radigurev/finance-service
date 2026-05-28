# SDD-INFRA-005 — Generic Filtering

> Status: Planned
> Owner: Platform
> Last updated: 2026-05-28
> Category: Infrastructure
> Related: SDD-INFRA-001, all list endpoints
> Mirrors: Warehouse `Warehouse.GenericFiltering`

---

## 1. Context & Scope

This spec defines `Finance.GenericFiltering`, a reusable library for translating client-supplied filter / sort / paginate parameters into `IQueryable<T>` expressions over EF Core entities. It is the same shape as `Warehouse.GenericFiltering` so that frontend filter components, query-string conventions, and OpenAPI examples can be shared 1:1.

**In scope:**
- `FilterRequest` model: `Filters`, `Sort`, `Page`, `PageSize`, `Search`
- `[Filterable]` attribute on entity properties that opts them into client-facing filtering
- `[Sortable]` attribute that opts properties into client-facing sorting
- Operator set: `eq`, `neq`, `gt`, `gte`, `lt`, `lte`, `contains`, `startsWith`, `endsWith`, `in`, `between`, `isNull`, `isNotNull`
- Type-safe expression building for `string`, `int`, `long`, `decimal`, `DateTimeOffset`, `Guid`, `bool`, `enum`
- `IQueryable<T>.ApplyFilter(request)` extension that composes Where + OrderBy + Skip + Take
- Stable error responses (ProblemDetails) for unknown / non-filterable properties or invalid operators

**Out of scope:**
- Server-side aggregation (count/sum/avg) — separate spec
- Full-text search backends (Elastic, Meilisearch) — `Search` is a simple `LIKE '%...%'` ORed across `[Searchable]` properties
- Field-level RBAC (filtering does NOT bypass authorization; permission is checked at the endpoint)

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
- The library MUST always append `OrderBy(x => x.Id)` (or the entity's primary key) as the last sort term so pagination is deterministic when the client-supplied sort produces ties.

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

Constants live in `Finance.Common.ErrorCodes.FilterErrorCodes`.

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

## 7. Open Items

- Boolean AND/OR composition between filters: today all filters are AND'd together. Adding `OR` requires a structured `groups` model — deferred.
- Polymorphic enum value matching by name vs ordinal (`AccountType.Asset` vs `1`). Today both work; codify in v2.
