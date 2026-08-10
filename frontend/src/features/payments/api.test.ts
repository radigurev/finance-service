import { describe, it, expect, vi, beforeEach } from 'vitest';
import { api } from '@/shared/api/axios';
import {
  buildAgingParams,
  buildCounterpartyBalanceParams,
  buildDeallocateParams,
  buildOpenItemParams,
  deallocatePayment,
  getAgingReport,
  searchOpenItems
} from './api';

vi.mock('@/shared/api/axios', () => ({
  api: {
    get: vi.fn().mockResolvedValue({ data: {} }),
    post: vi.fn().mockResolvedValue({ data: {} }),
    put: vi.fn().mockResolvedValue({ data: {} }),
    delete: vi.fn().mockResolvedValue({ data: {} })
  }
}));

const getMock = vi.mocked(api.get);
const deleteMock = vi.mocked(api.delete);

const COUNTERPARTY = '22222222-2222-2222-2222-222222222222';

describe('Payments API wire contract (SDD-UI-FIN-002 §1.4 traps 7, 8, 9, 15)', () => {
  beforeEach(() => {
    getMock.mockClear();
    deleteMock.mockClear();
  });

  it('Aging_BucketsSerializedAsRepeatedQueryValues_NotBracketedNorCommaSeparated', () => {
    const params = buildAgingParams({
      asOfDate: '2026-07-01',
      direction: 'AR',
      buckets: [30, 60, 90]
    });
    const query: string = params.toString();

    // The shipped `int[]? Buckets` binds ONLY from repeated values.
    expect(query).toContain('buckets=30');
    expect(query).toContain('buckets=60');
    expect(query).toContain('buckets=90');
    expect(params.getAll('buckets')).toEqual(['30', '60', '90']);

    // Axios' default array form would emit `buckets[]=30`, which ASP.NET Core will NOT bind…
    expect(query).not.toContain('buckets%5B%5D');
    expect(query).not.toContain('buckets[]');
    // …and a comma-separated form would need a model binder that does not exist.
    expect(query).not.toContain('30%2C60');
    expect(query).not.toContain('30,60');
  });

  it('Aging_OmitsBucketsParamWhenNotCustomized', () => {
    const notCustomized = buildAgingParams({ asOfDate: '2026-07-01', direction: 'AP' });
    expect(notCustomized.has('buckets')).toBe(false);
    expect(notCustomized.toString()).not.toContain('buckets');

    const emptyList = buildAgingParams({ asOfDate: '2026-07-01', direction: 'AP', buckets: [] });
    expect(emptyList.has('buckets')).toBe(false);
  });

  it('Aging_SendsNoFilterRequest_AndDoesNotPageClientSideAsIfServerPaged', async () => {
    await getAgingReport({
      asOfDate: '2026-07-01',
      direction: 'AR',
      counterpartyId: COUNTERPARTY,
      currencyCode: 'EUR'
    });

    expect(getMock).toHaveBeenCalledTimes(1);
    const [url, config] = getMock.mock.calls[0] as [string, { params: URLSearchParams }];
    expect(url).toBe('/aging');

    // The endpoint binds NO FilterRequest at all — nothing paging-shaped may be emitted.
    const query: string = config.params.toString();
    expect(query).toBe(
      'asOfDate=2026-07-01&direction=AR&counterpartyId=22222222-2222-2222-2222-222222222222&currencyCode=EUR'
    );
    expect(query).not.toContain('Page');
    expect(query).not.toContain('PageSize');
    expect(query).not.toContain('Sort');
    expect(query).not.toContain('Search');
  });

  it('OpenItems_MergesFilterRequestWithQueryNarrowings', async () => {
    await searchOpenItems(
      {
        asOfDate: '2026-07-01',
        direction: 'AR',
        counterpartyId: COUNTERPARTY,
        currencyCode: 'BGN',
        overdueOnly: true
      },
      { page: 2, pageSize: 50, sort: [{ field: 'dueDate', direction: 'asc' }] }
    );

    expect(getMock).toHaveBeenCalledTimes(1);
    const [url, config] = getMock.mock.calls[0] as [string, { params: Record<string, string> }];
    expect(url).toBe('/open-items');

    // `toFilterParams` alone carries only the paging half; this endpoint binds BOTH records from the
    // SAME query string, so both halves must be present (§1.4 trap 8).
    expect(config.params).toEqual({
      Page: '2',
      PageSize: '50',
      'Sort[0].Field': 'dueDate',
      'Sort[0].Direction': 'asc',
      asOfDate: '2026-07-01',
      direction: 'AR',
      counterpartyId: COUNTERPARTY,
      currencyCode: 'BGN',
      overdueOnly: 'true'
    });
  });

  it('omits every open-item narrowing the operator left blank', () => {
    const params = buildOpenItemParams({ overdueOnly: false }, { page: 1, pageSize: 25 });
    expect(params).toEqual({ Page: '1', PageSize: '25' });
  });

  it('merges the required as-of date and direction into the balances query', () => {
    const params = buildCounterpartyBalanceParams(
      { asOfDate: '2026-07-01', direction: 'AP' },
      { page: 1, pageSize: 50 }
    );
    expect(params).toEqual({
      Page: '1',
      PageSize: '50',
      asOfDate: '2026-07-01',
      direction: 'AP'
    });
  });

  it('Deallocate_SendsRowVersionAndReasonAsQueryParams_NotBody', async () => {
    await deallocatePayment('11111111-1111-1111-1111-111111111111', 7, {
      rowVersion: 'AAAA',
      reason: 'wrong amount'
    });

    expect(deleteMock).toHaveBeenCalledTimes(1);
    const call = deleteMock.mock.calls[0] as [string, { params: Record<string, string> }];
    expect(call[0]).toBe('/payments/11111111-1111-1111-1111-111111111111/allocations/7');

    // `rowVersion` and `reason` are `[FromQuery]` on the shipped controller — a DELETE here has NO
    // body, so axios must receive them as `params` and nothing else (§1.4 trap 9).
    expect(call[1].params).toEqual({ rowVersion: 'AAAA', reason: 'wrong amount' });
    expect(Object.keys(call[1])).toEqual(['params']);
    expect(call).toHaveLength(2);

    // Both are optional; omitting them yields an empty param set, never a body.
    expect(buildDeallocateParams({})).toEqual({});
    expect(buildDeallocateParams({ rowVersion: 'BBBB' })).toEqual({ rowVersion: 'BBBB' });
  });
});
