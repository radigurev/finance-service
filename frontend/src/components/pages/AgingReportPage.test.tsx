import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { AxiosError } from 'axios';
import { renderWithProviders } from '@/test/renderWithProviders';
import { AgingReportPage } from './AgingReportPage';
import { PaymentsListPage } from './PaymentsListPage';
import {
  getAgingReport,
  searchCounterpartyBalances,
  searchPayments
} from '@/features/payments/api';
import {
  PaymentDirection,
  PaymentDocumentType,
  PaymentMethod,
  PaymentStatus,
  type AgingReportDto,
  type AgingRowDto,
  type CounterpartyBalanceDto,
  type PaymentDto
} from '@/features/payments/types';
import type { PagedResult } from '@/shared/api/paging';

vi.mock('@/features/payments/api');
vi.mock('@/features/accounts/api');

/** Spied router navigation — MemoryRouter never touches `window.location`. */
const navigateMock = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => navigateMock };
});

vi.mock('@/shared/hooks/useNomenclature', () => ({
  useNomenclature: () => ({
    countries: [],
    currencies: [],
    isLoading: false,
    getStates: vi.fn(),
    getCities: vi.fn()
  })
}));

const getAgingReportMock = vi.mocked(getAgingReport);
const searchCounterpartyBalancesMock = vi.mocked(searchCounterpartyBalances);
const searchPaymentsMock = vi.mocked(searchPayments);

const COUNTERPARTY_A = '22222222-2222-2222-2222-222222222222';
const COUNTERPARTY_B = '33333333-3333-3333-3333-333333333333';

/** The default 30/60/90 bucket set the server applies when `buckets` is omitted. */
const DEFAULT_LABELS: string[] = ['Current', '1-30', '31-60', '61-90', '90+'];

function bucketAmounts(labels: string[], outstanding: number) {
  return labels.map((label, index) => ({
    label,
    fromDaysPastDue: index === 0 ? null : index,
    toDaysPastDue: index === labels.length - 1 ? null : index * 30,
    outstanding: index === 1 ? outstanding : 0,
    baseOutstanding: index === 1 ? outstanding : 0,
    itemCount: index === 1 ? 1 : 0
  }));
}

function agingRow(over: Partial<AgingRowDto> = {}): AgingRowDto {
  return {
    counterpartyId: COUNTERPARTY_A,
    currencyCode: 'BGN',
    baseCurrencyCode: 'BGN',
    openItemCount: 1,
    buckets: bucketAmounts(DEFAULT_LABELS, 100),
    totalOutstanding: 100,
    totalBaseOutstanding: 100,
    ...over
  };
}

function report(over: Partial<AgingReportDto> = {}): AgingReportDto {
  const labels: string[] = over.bucketLabels ?? DEFAULT_LABELS;
  return {
    asOfDate: '2026-07-01T00:00:00+00:00',
    direction: 'AR',
    baseCurrencyCode: 'BGN',
    bucketDayBoundaries: [30, 60, 90],
    bucketLabels: labels,
    rows: [agingRow()],
    totals: labels.map((label, index) => ({
      label,
      fromDaysPastDue: index === 0 ? null : index,
      toDaysPastDue: index === labels.length - 1 ? null : index * 30,
      baseOutstanding: index === 1 ? 100 : 0,
      itemCount: index === 1 ? 1 : 0
    })),
    grandTotalBaseOutstanding: 100,
    openItemCount: 1,
    ...over
  };
}

function balance(over: Partial<CounterpartyBalanceDto> = {}): CounterpartyBalanceDto {
  return {
    counterpartyId: COUNTERPARTY_A,
    currencyCode: 'BGN',
    baseCurrencyCode: 'BGN',
    direction: 'AR',
    openItemCount: 2,
    outstanding: 250,
    baseOutstanding: 250,
    overdueOutstanding: 100,
    baseOverdueOutstanding: 100,
    oldestDueDate: '2026-05-01T00:00:00+00:00',
    ...over
  };
}

function pagedBalances(items: CounterpartyBalanceDto[]): PagedResult<CounterpartyBalanceDto> {
  return { items, totalCount: items.length, page: 1, pageSize: 50 };
}

function payment(): PaymentDto {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    documentNumber: 'RCT-2026-000001',
    documentType: PaymentDocumentType.CustomerReceipt,
    direction: PaymentDirection.AR,
    method: PaymentMethod.BankTransfer,
    status: PaymentStatus.Posted,
    counterpartyId: COUNTERPARTY_A,
    currencyCode: 'BGN',
    baseCurrencyCode: 'BGN',
    amount: 100,
    exchangeRate: 1,
    baseAmount: 100,
    settlementAccountId: 7,
    paymentDate: '2026-07-01T00:00:00+00:00',
    bankReference: null,
    allocatedAmount: 0,
    unallocatedAmount: 100,
    journalEntryId: '99999999-9999-9999-9999-999999999999',
    cancellationReason: null,
    createdAt: '2026-07-01T00:00:00+00:00',
    confirmedAt: '2026-07-01T00:00:00+00:00',
    postedAt: '2026-07-01T00:00:00+00:00',
    reversedAt: null,
    rowVersion: 'AAAA'
  };
}

/** Builds an Axios failure carrying a ProblemDetails `title` — the machine error code. */
function problem(status: number, title: string): AxiosError {
  return new AxiosError('failed', undefined, undefined, undefined, {
    status,
    data: { title }
  } as never);
}

describe('AgingReportPage (SDD-UI-FIN-002 §2.14, §2.15)', () => {
  beforeEach(() => {
    getAgingReportMock.mockReset();
    searchCounterpartyBalancesMock.mockReset();
    searchPaymentsMock.mockReset();
    navigateMock.mockReset();
  });

  it('Aging_ColumnsBuiltFromResponseBucketLabels_NotHardCodedFive', async () => {
    // The default set is five labels; they come from the RESPONSE, and only `Current` is translated.
    getAgingReportMock.mockResolvedValue(report());

    renderWithProviders(<AgingReportPage />);

    for (const label of DEFAULT_LABELS) {
      expect(await screen.findByRole('columnheader', { name: label })).toBeInTheDocument();
    }
    // The numeric range labels render VERBATIM — they are server data, not i18n keys.
    expect(screen.queryByText('aging.bucket_1-30')).not.toBeInTheDocument();
  });

  it('Aging_FourBoundaries_RendersSixBucketColumns', async () => {
    // Configurability is real: four boundaries yield SIX labels, so six bucket columns must render.
    const sixLabels: string[] = ['Current', '1-15', '16-30', '31-60', '61-90', '90+'];
    getAgingReportMock.mockResolvedValue(
      report({
        bucketDayBoundaries: [15, 30, 60, 90],
        bucketLabels: sixLabels,
        rows: [agingRow({ buckets: bucketAmounts(sixLabels, 100) })]
      })
    );

    renderWithProviders(<AgingReportPage />);

    for (const label of sixLabels) {
      expect(await screen.findByRole('columnheader', { name: label })).toBeInTheDocument();
    }
    // The five-column default set is NOT hard-coded anywhere.
    expect(screen.queryByRole('columnheader', { name: '61-90' })).toBeInTheDocument();
    expect(screen.queryByRole('columnheader', { name: '1-30' })).not.toBeInTheDocument();
  });

  it('Aging_SendsNoFilterRequest_AndDoesNotPageClientSideAsIfServerPaged', async () => {
    getAgingReportMock.mockResolvedValue(report());

    renderWithProviders(<AgingReportPage />);

    await waitFor(() => expect(getAgingReportMock).toHaveBeenCalled());
    const query = getAgingReportMock.mock.calls[0][0] as unknown as Record<string, unknown>;
    // The endpoint has no paging contract, so nothing paging-shaped may be sent…
    expect(query).not.toHaveProperty('page');
    expect(query).not.toHaveProperty('pageSize');
    expect(query).not.toHaveProperty('sort');
    // …and `buckets` is omitted while the operator has not customized it, so the server default applies.
    expect(query.buckets).toBeUndefined();
    // …and the grid renders the whole set with no footer pagination.
    expect(document.querySelector('.MuiDataGrid-footerContainer')).toBeNull();
  });

  it('Aging_NoCrossCurrencyTransactionalTotalRendered', async () => {
    // Two currencies for the same counterparty produce two rows that must NOT be merged, and their
    // transactional amounts must NOT be summed — only base-currency figures may be.
    getAgingReportMock.mockResolvedValue(
      report({
        rows: [
          agingRow({ currencyCode: 'EUR', totalOutstanding: 100, totalBaseOutstanding: 195.58 }),
          agingRow({
            counterpartyId: COUNTERPARTY_B,
            currencyCode: 'USD',
            totalOutstanding: 200,
            totalBaseOutstanding: 340.12
          })
        ],
        grandTotalBaseOutstanding: 535.7
      })
    );

    renderWithProviders(<AgingReportPage />);

    expect(await screen.findByText(/535[.,]70/)).toBeInTheDocument();
    expect(await screen.findByText('Grand total (base)')).toBeInTheDocument();
    // 100 EUR + 200 USD = "300.00" is meaningless and must never appear.
    expect(screen.queryByText(/300[.,]00/)).not.toBeInTheDocument();
    // The totals row names the base currency it is denominated in.
    expect(await screen.findByText(/Report totals · BGN/)).toBeInTheDocument();
    expect(await screen.findByText(/Only base-currency figures are summed/i)).toBeInTheDocument();
  });

  it('Aging_EmptyReport_RendersEmptyStateWithZeroTotals_Not404Copy', async () => {
    getAgingReportMock.mockResolvedValue(
      report({
        rows: [],
        totals: DEFAULT_LABELS.map((label) => ({
          label,
          fromDaysPastDue: null,
          toDaysPastDue: null,
          baseOutstanding: 0,
          itemCount: 0
        })),
        grandTotalBaseOutstanding: 0,
        openItemCount: 0
      })
    );

    renderWithProviders(<AgingReportPage />);

    expect(await screen.findByText('Nothing outstanding as of this date.')).toBeInTheDocument();
    expect(await screen.findByText(/left out of the report entirely/i)).toBeInTheDocument();
    // Empty is a 200: zero totals, never a not-found message and never an error toast.
    const zeros = await screen.findAllByText(/^0[.,]00$/);
    expect(zeros.length).toBeGreaterThan(0);
    expect(screen.queryByText(/not found/i)).not.toBeInTheDocument();
    expect(document.querySelector('.notistack-MuiContent-error')).toBeNull();
  });

  it('Aging_RowDrillDown_NavigatesToOpenItemsWithNarrowings', async () => {
    getAgingReportMock.mockResolvedValue(
      report({ rows: [agingRow({ counterpartyId: COUNTERPARTY_B, currencyCode: 'EUR' })] })
    );

    const { user } = renderWithProviders(<AgingReportPage />);

    await user.click(await screen.findByText(COUNTERPARTY_B));

    // The drill-down carries exactly the narrowings `/open-items` supports.
    await waitFor(() => expect(navigateMock).toHaveBeenCalled());
    const target = navigateMock.mock.calls[0][0] as { pathname: string; search: string };
    expect(target.pathname).toBe('/open-items');
    expect(target.search).toContain(`counterpartyId=${COUNTERPARTY_B}`);
    expect(target.search).toContain('currencyCode=EUR');
    expect(target.search).toContain('direction=AR');
    expect(target.search).toContain('asOfDate=');
  });

  it('Aging_PeriodAgnosticAndInvoiceOnlyHints_AreRendered', async () => {
    getAgingReportMock.mockResolvedValue(report());

    renderWithProviders(<AgingReportPage />);

    expect(await screen.findByText(/ignores fiscal-period status/i)).toBeInTheDocument();
    expect(await screen.findByText(/no balance is ever negative/i)).toBeInTheDocument();
    expect(await screen.findByText(/strictly ascending, strictly positive/i)).toBeInTheDocument();
  });

  it('Balances_GridExposesNoUserSorting', async () => {
    searchCounterpartyBalancesMock.mockResolvedValue(pagedBalances([balance()]));

    const { user } = renderWithProviders(<AgingReportPage />);

    await user.click(await screen.findByRole('button', { name: 'Counterparty Balances' }));

    // The rows are GROUPED, so no [Sortable] entity surface applies; the server fixes the order.
    for (const header of ['Counterparty', 'Currency', 'Outstanding', 'Overdue', 'Oldest due']) {
      const node = await screen.findByRole('columnheader', { name: header });
      expect(node.className).not.toContain('columnHeader--sortable');
    }
    expect(await screen.findByText(/Column sorting is not available/i)).toBeInTheDocument();

    await waitFor(() => expect(searchCounterpartyBalancesMock).toHaveBeenCalled());
    const [query, request] = searchCounterpartyBalancesMock.mock.calls[0];
    expect(request.sort).toBeUndefined();
    expect(query.direction).toBe('AR');
    expect(query.asOfDate).toBeTruthy();
    // There is deliberately no counterparty narrowing on this endpoint.
    expect(query).not.toHaveProperty('counterpartyId');
  });

  it('Balances_NullOldestDueDate_RendersNoOpenItemsPlaceholder', async () => {
    searchCounterpartyBalancesMock.mockResolvedValue(
      pagedBalances([balance({ oldestDueDate: null })])
    );

    const { user } = renderWithProviders(<AgingReportPage />);

    await user.click(await screen.findByRole('button', { name: 'Counterparty Balances' }));

    expect(await screen.findByText('No open items')).toBeInTheDocument();
    expect(await screen.findByText(/sum of the non-current ageing buckets/i)).toBeInTheDocument();
  });

  it('Balances_RequiresAsOfDateAndDirection_ShowsValidationWhenMissing', async () => {
    searchCounterpartyBalancesMock.mockResolvedValue(pagedBalances([balance()]));

    const { user } = renderWithProviders(<AgingReportPage />);

    await user.click(await screen.findByRole('button', { name: 'Counterparty Balances' }));
    await waitFor(() => expect(searchCounterpartyBalancesMock).toHaveBeenCalled());
    searchCounterpartyBalancesMock.mockClear();

    // Clearing the required as-of date blocks the request client-side with an inline message.
    const asOf = document.querySelector('input[type="date"]') as HTMLInputElement;
    await user.clear(asOf);

    expect(await screen.findByText('An as-of date is required.')).toBeInTheDocument();
    expect(searchCounterpartyBalancesMock).not.toHaveBeenCalled();
  });

  it('Permissions_AgingForbiddenButPaymentsAllowed_SurfacesIndependently', async () => {
    // `finance.aging:read` is a SEPARATE permission from `finance.payment:read`, so a caller may
    // legitimately see payments while the ageing report is forbidden. Each surface must reach its own
    // conclusion from its OWN response — never infer one from the other (§2.17).
    getAgingReportMock.mockRejectedValue(problem(403, 'FORBIDDEN'));
    searchPaymentsMock.mockResolvedValue({
      items: [payment()],
      totalCount: 1,
      page: 1,
      pageSize: 50
    });

    const aging = renderWithProviders(<AgingReportPage />);
    expect(
      await screen.findByText('You do not have permission to view the ageing report.')
    ).toBeInTheDocument();
    expect(
      await screen.findByText(/separate reporting permission from the payment records/i)
    ).toBeInTheDocument();
    expect(screen.queryByText('403')).not.toBeInTheDocument();
    aging.unmount();

    renderWithProviders(<PaymentsListPage />);
    expect(await screen.findByText('RCT-2026-000001')).toBeInTheDocument();
    expect(
      screen.queryByText('You do not have permission to view payments.')
    ).not.toBeInTheDocument();
  });

  it('blocks a bucket boundary list the server would reject, before any request', async () => {
    getAgingReportMock.mockResolvedValue(report());

    renderWithProviders(<AgingReportPage />);

    await waitFor(() => expect(getAgingReportMock).toHaveBeenCalled());
    getAgingReportMock.mockClear();

    // Committed in one change so the only state observed is the invalid one.
    fireEvent.change(await screen.findByPlaceholderText('30, 60, 90'), {
      target: { value: '60, 30' }
    });

    expect(
      await screen.findByText('The bucket boundaries must be strictly ascending.')
    ).toBeInTheDocument();
    expect(getAgingReportMock).not.toHaveBeenCalled();

    // Six is the cap, and every boundary must be strictly positive.
    fireEvent.change(screen.getByPlaceholderText('30, 60, 90'), {
      target: { value: '5, 10, 15, 20, 25, 30, 35' }
    });
    expect(
      await screen.findByText('At most six bucket boundaries are allowed.')
    ).toBeInTheDocument();

    fireEvent.change(screen.getByPlaceholderText('30, 60, 90'), { target: { value: '0, 30' } });
    expect(
      await screen.findByText('Each bucket boundary must be a positive whole number of days.')
    ).toBeInTheDocument();
    expect(getAgingReportMock).not.toHaveBeenCalled();
  });

  it('Aging_MultiKeystrokeBucketEntry_IssuesExactlyOneUnboundedReportRequest', async () => {
    // `GET /api/v1/aging` has NO paging and NO server-side cap (§1.6 gap 8), so an undebounced field
    // rebuilds the WHOLE report per keystroke — typing `30, 60, 90` fired three successive requests.
    // The commits below land inside one debounce window, so exactly one request may follow.
    getAgingReportMock.mockResolvedValue(report());

    renderWithProviders(<AgingReportPage />);

    await waitFor(() => expect(getAgingReportMock).toHaveBeenCalled());
    getAgingReportMock.mockClear();

    const field = await screen.findByPlaceholderText('30, 60, 90');
    for (const value of ['3', '30', '30,', '30, ', '30, 6', '30, 60', '30, 60,', '30, 60, 9', '30, 60, 90']) {
      fireEvent.change(field, { target: { value } });
    }

    await waitFor(() => expect(getAgingReportMock).toHaveBeenCalledTimes(1));
    expect(getAgingReportMock.mock.calls[0][0].buckets).toEqual([30, 60, 90]);

    // And it settles there: no trailing request for an intermediate value arrives afterwards.
    await new Promise((resolve) => setTimeout(resolve, 400));
    expect(getAgingReportMock).toHaveBeenCalledTimes(1);
  });

  it('Aging_EmptyReport_EmptyStateIsNotClippedByTheGridOverlay', async () => {
    // The overlay-hosted empty state clipped this description by 22px — the worst of the three routes.
    getAgingReportMock.mockResolvedValue(report({ rows: [], grandTotalBaseOutstanding: 0 }));

    renderWithProviders(<AgingReportPage />);

    const description = await screen.findByText(/left out of the report entirely/i);
    let node: HTMLElement | null = description.parentElement;
    while (node && node !== document.body) {
      expect(node.className).not.toContain('MuiDataGrid-virtualScroller');
      expect(node.className).not.toContain('MuiDataGrid-overlayWrapper');
      node = node.parentElement;
    }
    expect(document.querySelector('.MuiDataGrid-virtualScroller')).toBeNull();
    // The report totals still render below it — an empty report is a 200 with zero totals.
    expect(await screen.findByText(/Report totals · BGN/)).toBeInTheDocument();
  });

  it('sends a customized bucket list once it is valid', async () => {
    getAgingReportMock.mockResolvedValue(report());

    renderWithProviders(<AgingReportPage />);

    await waitFor(() => expect(getAgingReportMock).toHaveBeenCalled());
    getAgingReportMock.mockClear();

    fireEvent.change(await screen.findByPlaceholderText('30, 60, 90'), {
      target: { value: '15, 45' }
    });

    await waitFor(() => expect(getAgingReportMock).toHaveBeenCalled());
    const last = getAgingReportMock.mock.calls[getAgingReportMock.mock.calls.length - 1][0];
    expect(last.buckets).toEqual([15, 45]);
  });
});
