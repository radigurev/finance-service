import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { AxiosError } from 'axios';
import { renderWithProviders } from '@/test/renderWithProviders';
import { FiscalPeriodsListPage } from './FiscalPeriodsListPage';
import { searchPeriods } from '@/features/periods/api';
import { FiscalPeriodStatus, type FiscalPeriodDto } from '@/features/periods/types';
import type { PagedResult } from '@/shared/api/paging';

vi.mock('@/features/periods/api');

const searchPeriodsMock = vi.mocked(searchPeriods);

function pagedPeriods(items: FiscalPeriodDto[]): PagedResult<FiscalPeriodDto> {
  return { items, totalCount: items.length, page: 1, pageSize: 50 };
}

const samplePeriod: FiscalPeriodDto = {
  id: 1,
  fiscalYear: 2026,
  periodNumber: 1,
  name: 'January 2026',
  startDate: '2026-01-01T00:00:00+00:00',
  endDate: '2026-01-31T00:00:00+00:00',
  status: FiscalPeriodStatus.Open,
  closedAt: null,
  reopenedAt: null,
  rowVersion: 'AAAA'
};

describe('FiscalPeriodsListPage', () => {
  beforeEach(() => {
    searchPeriodsMock.mockReset();
  });

  it('renders the page title and a returned period row', async () => {
    searchPeriodsMock.mockResolvedValue(pagedPeriods([samplePeriod]));

    renderWithProviders(<FiscalPeriodsListPage />);

    expect(await screen.findByText('Periods')).toBeInTheDocument();
    expect(await screen.findByText('January 2026')).toBeInTheDocument();
    expect(screen.getByText('2026')).toBeInTheDocument();
  });

  it('issues the server-side filter request with page 1 and the default page size', async () => {
    searchPeriodsMock.mockResolvedValue(pagedPeriods([samplePeriod]));

    renderWithProviders(<FiscalPeriodsListPage />);

    await waitFor(() => expect(searchPeriodsMock).toHaveBeenCalled());
    const request = searchPeriodsMock.mock.calls[0][0];
    expect(request.page).toBe(1);
    expect(request.pageSize).toBe(50);
  });

  it('shows the empty state when no periods are returned', async () => {
    searchPeriodsMock.mockResolvedValue(pagedPeriods([]));

    renderWithProviders(<FiscalPeriodsListPage />);

    expect(await screen.findByText('Periods')).toBeInTheDocument();
    await waitFor(() => expect(searchPeriodsMock).toHaveBeenCalled());
  });

  it('surfaces an error toast when the query fails', async () => {
    searchPeriodsMock.mockRejectedValue(
      new AxiosError('boom', undefined, undefined, undefined, {
        status: 500,
        data: { title: 'GENERIC_ERROR' }
      } as never)
    );

    renderWithProviders(<FiscalPeriodsListPage />);

    expect(await screen.findByText(/something went wrong/i)).toBeInTheDocument();
  });
});
