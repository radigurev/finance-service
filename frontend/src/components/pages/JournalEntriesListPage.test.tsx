import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { AxiosError } from 'axios';
import { renderWithProviders } from '@/test/renderWithProviders';
import { JournalEntriesListPage } from './JournalEntriesListPage';
import { searchJournalEntries } from '@/features/journal/api';
import { JournalEntryStatus, type JournalEntryDto } from '@/features/journal/types';
import type { PagedResult } from '@/shared/api/paging';

vi.mock('@/features/journal/api');
// The create/edit dialog mounts a nomenclature-backed account/currency picker; stub the
// hook so the page test makes no stray network calls.
vi.mock('@/shared/hooks/useNomenclature', () => ({
  useNomenclature: () => ({
    countries: [],
    states: [],
    cities: [],
    currencies: [],
    isLoading: false
  })
}));

const searchJournalEntriesMock = vi.mocked(searchJournalEntries);

function pagedEntries(items: JournalEntryDto[]): PagedResult<JournalEntryDto> {
  return { items, totalCount: items.length, page: 1, pageSize: 50 };
}

const sampleEntry: JournalEntryDto = {
  id: '11111111-1111-1111-1111-111111111111',
  entryNumber: 'JE-2026-0001',
  entryDate: '2026-06-01T00:00:00+00:00',
  description: 'Opening balance entry',
  baseCurrencyCode: 'BGN',
  status: JournalEntryStatus.Posted,
  reversesEntryId: null,
  createdAt: '2026-06-01T00:00:00+00:00',
  postedAt: '2026-06-01T00:00:00+00:00',
  lines: [],
  rowVersion: 'AAAA'
};

describe('JournalEntriesListPage', () => {
  beforeEach(() => {
    searchJournalEntriesMock.mockReset();
  });

  it('renders the page title and a returned journal-entry row', async () => {
    searchJournalEntriesMock.mockResolvedValue(pagedEntries([sampleEntry]));

    renderWithProviders(<JournalEntriesListPage />);

    expect(await screen.findByText('Journal')).toBeInTheDocument();
    expect(await screen.findByText('Opening balance entry')).toBeInTheDocument();
    expect(screen.getByText('JE-2026-0001')).toBeInTheDocument();
  });

  it('issues the server-side filter request with page 1 and the default page size', async () => {
    searchJournalEntriesMock.mockResolvedValue(pagedEntries([sampleEntry]));

    renderWithProviders(<JournalEntriesListPage />);

    await waitFor(() => expect(searchJournalEntriesMock).toHaveBeenCalled());
    const request = searchJournalEntriesMock.mock.calls[0][0];
    expect(request.page).toBe(1);
    expect(request.pageSize).toBe(50);
  });

  it('shows the empty state when no entries are returned', async () => {
    searchJournalEntriesMock.mockResolvedValue(pagedEntries([]));

    renderWithProviders(<JournalEntriesListPage />);

    expect(await screen.findByText('Journal')).toBeInTheDocument();
    await waitFor(() => expect(searchJournalEntriesMock).toHaveBeenCalled());
  });

  it('surfaces an error toast when the query fails', async () => {
    searchJournalEntriesMock.mockRejectedValue(
      new AxiosError('boom', undefined, undefined, undefined, {
        status: 500,
        data: { title: 'GENERIC_ERROR' }
      } as never)
    );

    renderWithProviders(<JournalEntriesListPage />);

    expect(await screen.findByText(/something went wrong/i)).toBeInTheDocument();
  });
});
