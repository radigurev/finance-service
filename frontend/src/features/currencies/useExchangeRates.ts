import { useQuery } from '@tanstack/react-query';
import { fetchLatestRate, fetchRateRange } from './api';
import type { ExchangeRateDto } from './types';

interface LatestRateArgs {
  currency: string;
  date: string;
  /** Disables the query until the inputs are complete. */
  enabled: boolean;
}

interface RateRangeArgs {
  currency: string;
  from: string;
  to: string;
  /** Disables the query until the inputs are complete and valid. */
  enabled: boolean;
}

/**
 * Reads the latest exchange rate on or before a date (SDD-NOM-001 §2.2). Transactional
 * read — `staleTime: 0` so it always re-hits the API; never cached server-side.
 */
export function useLatestRate({ currency, date, enabled }: LatestRateArgs) {
  return useQuery<ExchangeRateDto>({
    queryKey: ['exchange-rates', 'latest', currency, date],
    queryFn: () => fetchLatestRate(currency, date),
    enabled: enabled && currency !== '' && date !== '',
    staleTime: 0,
    retry: false
  });
}

/**
 * Reads an exchange-rate range ordered by date (SDD-NOM-001 §2.2). Transactional read —
 * `staleTime: 0`; never cached server-side.
 */
export function useRateRange({ currency, from, to, enabled }: RateRangeArgs) {
  return useQuery<ExchangeRateDto[]>({
    queryKey: ['exchange-rates', 'range', currency, from, to],
    queryFn: () => fetchRateRange(currency, from, to),
    enabled: enabled && currency !== '' && from !== '' && to !== '',
    staleTime: 0,
    retry: false
  });
}
