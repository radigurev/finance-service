import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { AxiosError } from 'axios';
import { renderWithProviders } from '@/test/renderWithProviders';
import { CurrenciesListPage } from './CurrenciesListPage';
import { searchCurrencies } from '@/features/currencies/api';
import type { CurrencyDto } from '@/features/currencies/types';
import type { PagedResult } from '@/shared/api/paging';

vi.mock('@/features/currencies/api');

const searchCurrenciesMock = vi.mocked(searchCurrencies);

function pagedCurrencies(items: CurrencyDto[]): PagedResult<CurrencyDto> {
  return { items, totalCount: items.length, page: 1, pageSize: 50 };
}

const sampleCurrency: CurrencyDto = {
  id: 1,
  isoCode: 'BGN',
  name: 'Bulgarian Lev',
  symbol: 'лв',
  isActive: true,
  rowVersion: 'AAAA'
};

describe('CurrenciesListPage', () => {
  beforeEach(() => {
    searchCurrenciesMock.mockReset();
  });

  it('renders the page title and a returned currency row', async () => {
    searchCurrenciesMock.mockResolvedValue(pagedCurrencies([sampleCurrency]));

    renderWithProviders(<CurrenciesListPage />);

    expect(await screen.findByText('Currencies')).toBeInTheDocument();
    expect(await screen.findByText('Bulgarian Lev')).toBeInTheDocument();
    expect(screen.getByText('BGN')).toBeInTheDocument();
  });

  it('issues the server-side filter request with page 1 and the default page size', async () => {
    searchCurrenciesMock.mockResolvedValue(pagedCurrencies([sampleCurrency]));

    renderWithProviders(<CurrenciesListPage />);

    await waitFor(() => expect(searchCurrenciesMock).toHaveBeenCalled());
    const request = searchCurrenciesMock.mock.calls[0][0];
    expect(request.page).toBe(1);
    expect(request.pageSize).toBe(50);
  });

  it('shows the empty state when no currencies are returned', async () => {
    searchCurrenciesMock.mockResolvedValue(pagedCurrencies([]));

    renderWithProviders(<CurrenciesListPage />);

    expect(await screen.findByText('Currencies')).toBeInTheDocument();
    await waitFor(() => expect(searchCurrenciesMock).toHaveBeenCalled());
  });

  it('surfaces an error toast when the query fails', async () => {
    searchCurrenciesMock.mockRejectedValue(
      new AxiosError('boom', undefined, undefined, undefined, {
        status: 500,
        data: { title: 'GENERIC_ERROR' }
      } as never)
    );

    renderWithProviders(<CurrenciesListPage />);

    expect(await screen.findByText(/something went wrong/i)).toBeInTheDocument();
  });
});
