import { describe, it, expect } from 'vitest';
import { toFilterParams, MAX_PAGE_SIZE, type FilterRequest } from './paging';

describe('toFilterParams', () => {
  it('maps page, pageSize and search to the ASP.NET binding shape', () => {
    const request: FilterRequest = { page: 2, pageSize: 25, search: 'cash' };

    expect(toFilterParams(request)).toEqual({
      Page: '2',
      PageSize: '25',
      Search: 'cash'
    });
  });

  it('omits search when empty and omits page/pageSize when undefined', () => {
    expect(toFilterParams({ search: '' })).toEqual({});
    expect(toFilterParams({})).toEqual({});
  });

  it('flattens indexed filters including operator and stringified value', () => {
    const request: FilterRequest = {
      filters: [{ field: 'code', operator: 'eq', value: 100 }]
    };

    expect(toFilterParams(request)).toEqual({
      'Filters[0].Field': 'code',
      'Filters[0].Operator': 'eq',
      'Filters[0].Value': '100'
    });
  });

  it('omits the Value key when a filter value is null or undefined', () => {
    const request: FilterRequest = {
      filters: [{ field: 'name', operator: 'isnull', value: null }]
    };

    const params = toFilterParams(request);

    expect(params['Filters[0].Field']).toBe('name');
    expect(params).not.toHaveProperty('Filters[0].Value');
  });

  it('flattens indexed sort clauses', () => {
    const request: FilterRequest = {
      sort: [
        { field: 'code', direction: 'asc' },
        { field: 'name', direction: 'desc' }
      ]
    };

    expect(toFilterParams(request)).toMatchObject({
      'Sort[0].Field': 'code',
      'Sort[0].Direction': 'asc',
      'Sort[1].Field': 'name',
      'Sort[1].Direction': 'desc'
    });
  });

  it('caps reference matches the backend SDD-INFRA-005 limit', () => {
    expect(MAX_PAGE_SIZE).toBe(200);
  });
});
