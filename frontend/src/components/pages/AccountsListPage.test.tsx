import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { AxiosError } from 'axios';
import { renderWithProviders } from '@/test/renderWithProviders';
import { AccountsListPage } from './AccountsListPage';
import { searchAccounts } from '@/features/accounts/api';
import { AccountType, type AccountDto } from '@/features/accounts/types';
import type { PagedResult } from '@/shared/api/paging';

vi.mock('@/features/accounts/api');
// The create/edit dialog mounts a nomenclature dropdown; stub it so the page test
// makes no stray network calls.
vi.mock('@/shared/hooks/useNomenclature', () => ({
  useNomenclature: () => ({
    countries: [],
    states: [],
    cities: [],
    currencies: [],
    isLoading: false
  })
}));

const searchAccountsMock = vi.mocked(searchAccounts);

function pagedAccounts(items: AccountDto[]): PagedResult<AccountDto> {
  return { items, totalCount: items.length, page: 1, pageSize: 50 };
}

const sampleAccount: AccountDto = {
  id: 1,
  code: '100',
  name: 'Cash on hand',
  type: AccountType.Asset,
  parentId: null,
  isActive: true,
  countryCode: 'BG',
  rowVersion: 'AAAA'
};

describe('AccountsListPage', () => {
  beforeEach(() => {
    searchAccountsMock.mockReset();
  });

  it('renders the page title and a returned account row', async () => {
    searchAccountsMock.mockResolvedValue(pagedAccounts([sampleAccount]));

    renderWithProviders(<AccountsListPage />);

    expect(await screen.findByText('Chart of Accounts')).toBeInTheDocument();
    expect(await screen.findByText('Cash on hand')).toBeInTheDocument();
    expect(screen.getByText('100')).toBeInTheDocument();
  });

  it('issues the server-side filter request with page 1 and the default page size', async () => {
    searchAccountsMock.mockResolvedValue(pagedAccounts([sampleAccount]));

    renderWithProviders(<AccountsListPage />);

    await waitFor(() => expect(searchAccountsMock).toHaveBeenCalled());
    const request = searchAccountsMock.mock.calls[0][0];
    expect(request.page).toBe(1);
    expect(request.pageSize).toBe(50);
  });

  it('shows the empty state when no accounts are returned', async () => {
    searchAccountsMock.mockResolvedValue(pagedAccounts([]));

    renderWithProviders(<AccountsListPage />);

    expect(await screen.findByText('Chart of Accounts')).toBeInTheDocument();
    await waitFor(() => expect(searchAccountsMock).toHaveBeenCalled());
  });

  it('surfaces an error toast when the query fails', async () => {
    searchAccountsMock.mockRejectedValue(
      new AxiosError('boom', undefined, undefined, undefined, {
        status: 500,
        data: { title: 'GENERIC_ERROR' }
      } as never)
    );

    renderWithProviders(<AccountsListPage />);

    // notistack renders the toast text into the DOM (errors.GENERIC_ERROR).
    expect(await screen.findByText(/something went wrong/i)).toBeInTheDocument();
  });
});
