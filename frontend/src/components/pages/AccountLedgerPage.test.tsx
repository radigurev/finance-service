import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import { AxiosError } from 'axios';
import { renderWithProviders } from '@/test/renderWithProviders';
import { AccountLedgerPage } from './AccountLedgerPage';
import { useAccountLedger } from '@/features/generalLedger/api';
import type { AccountLedgerDto } from '@/features/generalLedger/types';

vi.mock('@/features/generalLedger/api');

const useAccountLedgerMock = vi.mocked(useAccountLedger);

const sampleLedger: AccountLedgerDto = {
  accountId: 1,
  accountCode: '100',
  accountName: 'Cash on hand',
  fromDate: null,
  toDate: null,
  openingBalance: 0,
  closingBalance: 1500,
  lines: {
    items: [
      {
        lineId: 10,
        entryNumber: 'JE-2026-0001',
        entryDate: '2026-06-01T00:00:00+00:00',
        description: 'Opening cash',
        debit: 1500,
        credit: 0,
        runningBalance: 1500
      }
    ],
    totalCount: 1,
    page: 1,
    pageSize: 50
  }
};

type AccountLedgerQuery = ReturnType<typeof useAccountLedger>;

/** Builds a minimal TanStack-Query-shaped return value for the mocked hook. */
function queryResult(overrides: Partial<AccountLedgerQuery>): AccountLedgerQuery {
  return {
    data: undefined,
    error: null,
    isFetching: false,
    ...overrides
  } as AccountLedgerQuery;
}

function renderAtAccount(accountId: string) {
  return renderWithProviders(<AccountLedgerPage />, {
    initialEntries: [`/general-ledger/accounts/${accountId}`],
    routePath: 'general-ledger/accounts/:accountId'
  });
}

describe('AccountLedgerPage', () => {
  beforeEach(() => {
    useAccountLedgerMock.mockReset();
  });

  it('renders the page title and a returned ledger line', async () => {
    useAccountLedgerMock.mockReturnValue(queryResult({ data: sampleLedger }));

    renderAtAccount('1');

    expect(await screen.findByText('Account Ledger')).toBeInTheDocument();
    expect(await screen.findByText('Opening cash')).toBeInTheDocument();
    expect(screen.getByText('JE-2026-0001')).toBeInTheDocument();
  });

  it('resolves the accountId route param and calls the ledger hook with it', async () => {
    useAccountLedgerMock.mockReturnValue(queryResult({ data: sampleLedger }));

    renderAtAccount('42');

    await screen.findByText('Account Ledger');
    expect(useAccountLedgerMock).toHaveBeenCalled();
    const [accountId, args] = useAccountLedgerMock.mock.calls[0];
    expect(accountId).toBe(42);
    expect(args.page).toBe(1);
    expect(args.pageSize).toBe(50);
  });

  it('renders a well-formed empty ledger when no lines are returned', async () => {
    useAccountLedgerMock.mockReturnValue(
      queryResult({
        data: {
          ...sampleLedger,
          openingBalance: 0,
          closingBalance: 0,
          lines: { items: [], totalCount: 0, page: 1, pageSize: 50 }
        }
      })
    );

    renderAtAccount('1');

    expect(await screen.findByText('Account Ledger')).toBeInTheDocument();
    expect(useAccountLedgerMock).toHaveBeenCalled();
  });

  it('surfaces an error toast when the query fails', async () => {
    useAccountLedgerMock.mockReturnValue(
      queryResult({
        error: new AxiosError('boom', undefined, undefined, undefined, {
          status: 500,
          data: { title: 'GENERIC_ERROR' }
        } as never)
      })
    );

    renderAtAccount('1');

    expect(await screen.findByText(/something went wrong/i)).toBeInTheDocument();
  });
});
