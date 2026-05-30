/** Mirrors `Finance.GenericFiltering.Models.PagedResult<T>` (SDD-INFRA-005). */
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

/** Mirrors `FilterCriterion`: a single AND-combined filter clause. */
export interface FilterCriterion {
  field: string;
  operator: string;
  value: unknown;
}

/** Mirrors `SortCriterion`: an ordered sort clause. */
export interface SortCriterion {
  field: string;
  direction: 'asc' | 'desc';
}

/** Mirrors `FilterRequest`: the canonical filter / sort / page contract. */
export interface FilterRequest {
  filters?: FilterCriterion[];
  sort?: SortCriterion[];
  page?: number;
  pageSize?: number;
  search?: string;
}

/** The backend caps page size at 200 (SDD-INFRA-005). */
export const MAX_PAGE_SIZE = 200;

/**
 * Flattens a {@link FilterRequest} into the indexed query-string shape that ASP.NET Core
 * model binding expects for a `[FromQuery] FilterRequest` (e.g. `Filters[0].Field=code`).
 * Returns a plain record consumed as Axios `params`.
 */
export function toFilterParams(request: FilterRequest): Record<string, string> {
  const params: Record<string, string> = {};

  if (request.page !== undefined) {
    params['Page'] = String(request.page);
  }
  if (request.pageSize !== undefined) {
    params['PageSize'] = String(request.pageSize);
  }
  if (request.search) {
    params['Search'] = request.search;
  }

  (request.filters ?? []).forEach((filter, index) => {
    params[`Filters[${index}].Field`] = filter.field;
    params[`Filters[${index}].Operator`] = filter.operator;
    if (filter.value !== undefined && filter.value !== null) {
      params[`Filters[${index}].Value`] = String(filter.value);
    }
  });

  (request.sort ?? []).forEach((sort, index) => {
    params[`Sort[${index}].Field`] = sort.field;
    params[`Sort[${index}].Direction`] = sort.direction;
  });

  return params;
}
