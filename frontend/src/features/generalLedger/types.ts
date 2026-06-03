/**
 * Wire contracts for the General Ledger & Trial Balance feature (SDD-FIN-003).
 * These mirror the .NET `Finance.ServiceModel` GL read DTOs field-for-field — keep
 * names identical so the JSON deserializes without remapping. All monetary values are
 * decimals serialized as numbers; treat them as money (DECIMAL(18,2) — 2 decimal places).
 */

import type { PagedResult } from '@/shared/api/paging';

/** Mirrors `TrialBalanceRowDto`: one account's aggregated debit/credit roll-up. */
export interface TrialBalanceRowDto {
  accountId: number;
  accountCode?: string | null;
  accountName?: string | null;
  totalDebit: number;
  totalCredit: number;
  debitBalance: number;
  creditBalance: number;
}

/** Mirrors `TrialBalanceDto`: the as-of (and optional from) trial balance with grand totals. */
export interface TrialBalanceDto {
  /** ISO 8601 inclusive upper bound of the accounting date. */
  asOfDate: string;
  /** ISO 8601 inclusive lower bound; omitted means cumulative from the beginning of time. */
  fromDate?: string | null;
  rows: TrialBalanceRowDto[];
  grandTotalDebit: number;
  grandTotalCredit: number;
  /** True when `grandTotalDebit === grandTotalCredit` to the cent (SDD-FIN-001 §2.3). */
  balanced: boolean;
}

/** Mirrors `AccountLedgerLineDto`: a single posted line in an account's ledger. */
export interface AccountLedgerLineDto {
  lineId: number;
  entryNumber: string;
  /** ISO 8601 time-zone-aware accounting date. */
  entryDate: string;
  description?: string | null;
  debit: number;
  credit: number;
  runningBalance: number;
}

/** Mirrors `AccountLedgerDto`: a single account's ledger over a date window. */
export interface AccountLedgerDto {
  accountId: number;
  accountCode?: string | null;
  accountName?: string | null;
  fromDate?: string | null;
  toDate?: string | null;
  openingBalance: number;
  closingBalance: number;
  lines: PagedResult<AccountLedgerLineDto>;
}
