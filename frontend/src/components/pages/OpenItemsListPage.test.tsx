import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { AxiosError } from 'axios';
import { renderWithProviders } from '@/test/renderWithProviders';
import { OpenItemsListPage } from './OpenItemsListPage';
import { searchOpenItems } from '@/features/payments/api';
import { SettlementStatus, type OpenItemDto } from '@/features/payments/types';
import type { PagedResult } from '@/shared/api/paging';

vi.mock('@/features/payments/api');

const searchOpenItemsMock = vi.mocked(searchOpenItems);

const COUNTERPARTY = '22222222-2222-2222-2222-222222222222';

function paged(items: OpenItemDto[]): PagedResult<OpenItemDto> {
  return { items, totalCount: items.length, page: 1, pageSize: 50 };
}

function openItem(over: Partial<OpenItemDto> = {}): OpenItemDto {
  return {
    invoiceId: '44444444-4444-4444-4444-444444444444',
    documentNumber: 'SINV-2026-0001',
    documentType: 'SaleInvoice',
    direction: 'AR',
    counterpartyId: COUNTERPARTY,
    currencyCode: 'BGN',
    baseCurrencyCode: 'BGN',
    grossTotal: 120,
    settledAmount: 20,
    outstanding: 100,
    baseOutstanding: 100,
    issueDate: '2026-06-01T00:00:00+00:00',
    dueDate: '2026-06-15T00:00:00+00:00',
    daysPastDue: 12,
    agingBucket: '1-30',
    settlementStatus: SettlementStatus.PartiallySettled,
    invoiceStatus: 'Posted',
    ...over
  };
}

/** Builds an Axios failure carrying a ProblemDetails `title` — the machine error code. */
function problem(status: number, title: string): AxiosError {
  return new AxiosError('failed', undefined, undefined, undefined, {
    status,
    data: { title }
  } as never);
}

describe('OpenItemsListPage (SDD-UI-FIN-002 §2.13)', () => {
  beforeEach(() => {
    searchOpenItemsMock.mockReset();
  });

  it('OpenItems_MergesFilterRequestWithQueryNarrowings', async () => {
    searchOpenItemsMock.mockResolvedValue(paged([openItem()]));

    // The page carries the drill-down narrowings straight into the request alongside the paging half.
    renderWithProviders(<OpenItemsListPage />, {
      initialEntries: [
        `/open-items?counterpartyId=${COUNTERPARTY}&currencyCode=BGN&direction=AR&asOfDate=2026-07-01`
      ],
      routePath: '/open-items'
    });

    await waitFor(() => expect(searchOpenItemsMock).toHaveBeenCalled());
    const [narrowing, request] = searchOpenItemsMock.mock.calls[0];
    expect(narrowing).toEqual({
      asOfDate: '2026-07-01',
      direction: 'AR',
      counterpartyId: COUNTERPARTY,
      currencyCode: 'BGN',
      overdueOnly: false
    });
    expect(request.page).toBe(1);
    expect(request.pageSize).toBe(50);
    expect(request.pageSize).toBeLessThanOrEqual(200);
  });

  it('OpenItems_DefaultOrder_IsOldestDueFirst', async () => {
    searchOpenItemsMock.mockResolvedValue(paged([openItem()]));

    renderWithProviders(<OpenItemsListPage />);

    await waitFor(() => expect(searchOpenItemsMock).toHaveBeenCalled());
    // A collection worklist reads oldest-due-first.
    expect(searchOpenItemsMock.mock.calls[0][1].sort).toEqual([
      { field: 'dueDate', direction: 'asc' }
    ]);
  });

  it('OpenItems_NoSearchBoxRendered', async () => {
    searchOpenItemsMock.mockResolvedValue(paged([openItem()]));

    renderWithProviders(<OpenItemsListPage />);

    expect(await screen.findByText('Open Items')).toBeInTheDocument();
    // `InvoiceOpenItem` declares NO [Searchable] property, so `search` would match nothing.
    expect(screen.queryByPlaceholderText(/Search/i)).not.toBeInTheDocument();
    await waitFor(() => expect(searchOpenItemsMock).toHaveBeenCalled());
    expect(searchOpenItemsMock.mock.calls[0][1].search).toBeUndefined();
  });

  it('OpenItems_NotYetDue_RendersNotYetDueNotNegativeDays', async () => {
    searchOpenItemsMock.mockResolvedValue(
      paged([openItem({ daysPastDue: -4, agingBucket: 'Current' })])
    );

    renderWithProviders(<OpenItemsListPage />);

    expect(await screen.findByText('Not yet due')).toBeInTheDocument();
    expect(screen.queryByText('-4')).not.toBeInTheDocument();
    // Due exactly ON the as-of date is Current, never 1-30 — the bucket label is server data.
    expect(await screen.findByText('Current')).toBeInTheDocument();
  });

  it('renders zero days past due as not yet due, in the Current bucket', async () => {
    searchOpenItemsMock.mockResolvedValue(
      paged([openItem({ daysPastDue: 0, agingBucket: 'Current' })])
    );

    renderWithProviders(<OpenItemsListPage />);

    expect(await screen.findByText('Not yet due')).toBeInTheDocument();
    // The zero is never rendered as a bare day count.
    expect(screen.queryByText('0')).not.toBeInTheDocument();
  });

  it('OpenItems_EventualConsistencyAndCreditNoteHints_AreRendered', async () => {
    searchOpenItemsMock.mockResolvedValue(paged([openItem()]));

    renderWithProviders(<OpenItemsListPage />);

    expect(await screen.findByText(/catches up asynchronously/i)).toBeInTheDocument();
    expect(await screen.findByText(/absence is by design and not a gap/i)).toBeInTheDocument();
    // Base-currency columns are labelled as booking-rate figures, not a live revaluation.
    expect(await screen.findByText(/booking rate/i)).toBeInTheDocument();
  });

  it('OpenItems_FutureAsOfDate_BlockedClientSide_AndServerCodeMapped', async () => {
    searchOpenItemsMock.mockResolvedValue(paged([openItem()]));
    const tomorrow: string = new Date(Date.now() + 86_400_000).toISOString().slice(0, 10);

    const { unmount } = renderWithProviders(<OpenItemsListPage />, {
      initialEntries: [`/open-items?asOfDate=${tomorrow}`],
      routePath: '/open-items'
    });

    // Blocked client-side: the inline error shows and NO request is issued.
    expect(await screen.findByText('The as-of date cannot be in the future.')).toBeInTheDocument();
    expect(searchOpenItemsMock).not.toHaveBeenCalled();
    unmount();

    // And if a future date ever reaches the server, the code is mapped, never a raw status.
    searchOpenItemsMock.mockReset();
    searchOpenItemsMock.mockRejectedValue(problem(400, 'INVALID_AGING_AS_OF_DATE'));

    renderWithProviders(<OpenItemsListPage />);

    expect(
      await screen.findByText('The as-of date is required and cannot be in the future.')
    ).toBeInTheDocument();
  });

  it('renders an empty window as a 200 empty state rather than a not-found message', async () => {
    searchOpenItemsMock.mockResolvedValue(paged([]));

    renderWithProviders(<OpenItemsListPage />);

    expect(await screen.findByText('No open items in this window.')).toBeInTheDocument();
    expect(screen.queryByText(/not found/i)).not.toBeInTheDocument();
    expect(document.querySelector('.notistack-MuiContent-error')).toBeNull();
  });

  it('renders the editorial forbidden state on a 403 rather than a raw status', async () => {
    searchOpenItemsMock.mockRejectedValue(problem(403, 'FORBIDDEN'));

    renderWithProviders(<OpenItemsListPage />);

    expect(
      await screen.findByText('You do not have permission to view open items.')
    ).toBeInTheDocument();
    expect(screen.queryByText('403')).not.toBeInTheDocument();
    expect(document.querySelector('.notistack-MuiContent-error')).toBeNull();
  });
});
