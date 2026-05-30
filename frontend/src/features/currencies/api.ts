import { api } from '@/shared/api/axios';
import { toFilterParams, type FilterRequest, type PagedResult } from '@/shared/api/paging';
import type {
  CurrencyDto,
  CreateCurrencyRequest,
  UpdateCurrencyRequest,
  ExchangeRateDto
} from './types';

/** Lists currencies (active and inactive) as a paged envelope (SDD-NOM-001 §2.1). */
export async function searchCurrencies(request: FilterRequest): Promise<PagedResult<CurrencyDto>> {
  const { data } = await api.get<PagedResult<CurrencyDto>>('/currencies', {
    params: toFilterParams(request)
  });
  return data;
}

/** Creates a new currency and returns the persisted DTO. */
export async function createCurrency(request: CreateCurrencyRequest): Promise<CurrencyDto> {
  const { data } = await api.post<CurrencyDto>('/currencies', request);
  return data;
}

/**
 * Updates an existing currency. The `isoCode` is taken from the path (immutable), and the
 * captured `rowVersion` is round-tripped so a stale write is rejected with
 * `CONCURRENT_MODIFICATION` (SDD-NOM-001 §2.6).
 */
export async function updateCurrency(
  isoCode: string,
  request: UpdateCurrencyRequest
): Promise<CurrencyDto> {
  const { data } = await api.put<CurrencyDto>(`/currencies/${isoCode}`, request);
  return data;
}

/**
 * Returns the latest exchange rate on or before `date` for a currency. Transactional read —
 * never cached (SDD-NOM-001 §2.2). `date` is a `yyyy-MM-dd` string.
 */
export async function fetchLatestRate(currency: string, date: string): Promise<ExchangeRateDto> {
  const { data } = await api.get<ExchangeRateDto>('/exchange-rates/latest', {
    params: { currency, date }
  });
  return data;
}

/**
 * Returns the exchange-rate range for a currency, ordered ascending by date. `from`/`to`
 * are `yyyy-MM-dd` strings; `from` MUST be ≤ `to` (else `INVALID_DATE_RANGE`).
 */
export async function fetchRateRange(
  currency: string,
  from: string,
  to: string
): Promise<ExchangeRateDto[]> {
  const { data } = await api.get<ExchangeRateDto[]>('/exchange-rates/range', {
    params: { currency, from, to }
  });
  return data;
}
