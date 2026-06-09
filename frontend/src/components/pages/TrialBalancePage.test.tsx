import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import { AxiosError } from 'axios';
import { renderWithProviders } from '@/test/renderWithProviders';
import { TrialBalancePage } from './TrialBalancePage';
import { useTrialBalance } from '@/features/generalLedger/api';
import type { TrialBalanceDto } from '@/features/generalLedger/types';

vi.mock('@/features/generalLedger/api');

const useTrialBalanceMock = vi.mocked(useTrialBalance);

const sampleTrialBalance: TrialBalanceDto = {
  asOfDate: '2026-06-08T00:00:00+00:00',
  fromDate: null,
  rows: [
    {
      accountId: 1,
      accountCode: '100',
      accountName: 'Cash on hand',
      totalDebit: 1500,
      totalCredit: 0,
      debitBalance: 1500,
      creditBalance: 0
    }
  ],
  grandTotalDebit: 1500,
  grandTotalCredit: 1500,
  balanced: true
};

type TrialBalanceQuery = ReturnType<typeof useTrialBalance>;

/** Builds a minimal TanStack-Query-shaped return value for the mocked hook. */
function queryResult(overrides: Partial<TrialBalanceQuery>): TrialBalanceQuery {
  return {
    data: undefined,
    error: null,
    isFetching: false,
    ...overrides
  } as TrialBalanceQuery;
}

describe('TrialBalancePage', () => {
  beforeEach(() => {
    useTrialBalanceMock.mockReset();
  });

  it('renders the page title and a returned trial-balance row', async () => {
    useTrialBalanceMock.mockReturnValue(queryResult({ data: sampleTrialBalance }));

    renderWithProviders(<TrialBalancePage />);

    expect(await screen.findByText('Trial Balance')).toBeInTheDocument();
    expect(await screen.findByText('Cash on hand')).toBeInTheDocument();
    expect(screen.getByText('100')).toBeInTheDocument();
    expect(screen.getByText('Balanced')).toBeInTheDocument();
  });

  it('queries the trial balance for the default as-of date', async () => {
    useTrialBalanceMock.mockReturnValue(queryResult({ data: sampleTrialBalance }));

    renderWithProviders(<TrialBalancePage />);

    await screen.findByText('Trial Balance');
    expect(useTrialBalanceMock).toHaveBeenCalled();
    const [asOfDate] = useTrialBalanceMock.mock.calls[0];
    expect(asOfDate).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });

  it('shows the empty state when no rows are returned', async () => {
    useTrialBalanceMock.mockReturnValue(
      queryResult({
        data: { ...sampleTrialBalance, rows: [], grandTotalDebit: 0, grandTotalCredit: 0 }
      })
    );

    renderWithProviders(<TrialBalancePage />);

    expect(await screen.findByText('Trial Balance')).toBeInTheDocument();
    expect(await screen.findByText('No posted activity in this window.')).toBeInTheDocument();
  });

  it('surfaces an error toast when the query fails', async () => {
    useTrialBalanceMock.mockReturnValue(
      queryResult({
        error: new AxiosError('boom', undefined, undefined, undefined, {
          status: 500,
          data: { title: 'GENERIC_ERROR' }
        } as never)
      })
    );

    renderWithProviders(<TrialBalancePage />);

    expect(await screen.findByText(/something went wrong/i)).toBeInTheDocument();
  });
});
