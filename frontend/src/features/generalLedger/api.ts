import { useQuery } from '@tanstack/react-query';
import { api } from '@/shared/api/axios';
import type { TrialBalanceDto, AccountLedgerDto } from './types';

/** Query arguments for a single account's paged ledger. */
interface AccountLedgerArgs {
  fromDate?: string;
  toDate?: string;
  page: number;
  pageSize: number;
}

/**
 * Reads the trial balance for an as-of date and an optional from date (SDD-FIN-003 §2.2).
 * Omitting `fromDate` aggregates cumulatively from the beginning of time up to `asOfDate`.
 */
export async function fetchTrialBalance(
  asOfDate: string,
  fromDate?: string
): Promise<TrialBalanceDto> {
  const params: Record<string, string> = { asOfDate };
  if (fromDate) {
    params.fromDate = fromDate;
  }
  const { data } = await api.get<TrialBalanceDto>('/trial-balance', { params });
  return data;
}

/**
 * Reads a single account's ledger — opening balance, paged in-range lines, running balance,
 * closing balance (SDD-FIN-003 §2.3). The line list is paged (PageSize capped at 200).
 */
export async function fetchAccountLedger(
  accountId: number,
  args: AccountLedgerArgs
): Promise<AccountLedgerDto> {
  const params: Record<string, string> = {
    page: String(args.page),
    pageSize: String(args.pageSize)
  };
  if (args.fromDate) {
    params.fromDate = args.fromDate;
  }
  if (args.toDate) {
    params.toDate = args.toDate;
  }
  const { data } = await api.get<AccountLedgerDto>(
    `/general-ledger/accounts/${accountId}`,
    { params }
  );
  return data;
}

/**
 * Trial-balance query (SDD-FIN-003 §2.2). GL balances are derived from transactional data and
 * MUST NOT be cached (SDD-INFRA-004) — `staleTime: 0` so every param change re-hits the API.
 */
export function useTrialBalance(asOfDate: string, fromDate?: string) {
  return useQuery<TrialBalanceDto>({
    queryKey: ['trial-balance', asOfDate, fromDate ?? null],
    queryFn: () => fetchTrialBalance(asOfDate, fromDate),
    enabled: asOfDate !== '',
    staleTime: 0,
    retry: false
  });
}

/**
 * Account-ledger query (SDD-FIN-003 §2.3). Transactional read — `staleTime: 0`; refetches on
 * any param change. Disabled until a positive account id is supplied.
 */
export function useAccountLedger(accountId: number, args: AccountLedgerArgs) {
  return useQuery<AccountLedgerDto>({
    queryKey: [
      'account-ledger',
      accountId,
      args.fromDate ?? null,
      args.toDate ?? null,
      args.page,
      args.pageSize
    ],
    queryFn: () => fetchAccountLedger(accountId, args),
    enabled: Number.isInteger(accountId) && accountId > 0,
    staleTime: 0,
    retry: false,
    placeholderData: (prev) => prev
  });
}
