import { api } from '@/shared/api/axios';
import { toFilterParams, type PagedResult } from '@/shared/api/paging';
import type { CurrencyDto, CountryDto, StateDto, CityDto } from './types';

/**
 * Normalizes a response that may be either a bare array or a {@link PagedResult}
 * envelope into a flat array — reference endpoints are small and unpaged in the UI.
 */
function unwrap<T>(data: T[] | PagedResult<T>): T[] {
  return Array.isArray(data) ? data : data.items;
}

/** Raw currency row as serialized by the Finance Nomenclature API (`isoCode`). */
interface RawCurrency {
  isoCode: string;
  name: string;
  symbol?: string | null;
  isActive: boolean;
}

/**
 * Loads the currency reference list for dropdowns. Requests the full page (capped at the
 * backend max), keeps only active currencies, and normalizes `isoCode` → `code` so every
 * dropdown consumer reads a single stable {@link CurrencyDto} shape (SDD-NOM-001 §2.1).
 */
export async function fetchCurrencies(): Promise<CurrencyDto[]> {
  const { data } = await api.get<RawCurrency[] | PagedResult<RawCurrency>>('/currencies', {
    params: toFilterParams({ page: 1, pageSize: 200, sort: [{ field: 'isoCode', direction: 'asc' }] })
  });
  return unwrap(data)
    .filter((row) => row.isActive)
    .map((row) => ({ code: row.isoCode, name: row.name, symbol: row.symbol }));
}

/** Loads the country reference list (Warehouse proxy). */
export async function fetchCountries(): Promise<CountryDto[]> {
  const { data } = await api.get<CountryDto[] | PagedResult<CountryDto>>('/countries');
  return unwrap(data);
}

/** Loads states / provinces for a country. */
export async function fetchStates(countryCode: string): Promise<StateDto[]> {
  const { data } = await api.get<StateDto[] | PagedResult<StateDto>>('/states', {
    params: { country: countryCode }
  });
  return unwrap(data);
}

/** Loads cities for a state. */
export async function fetchCities(stateId: number): Promise<CityDto[]> {
  const { data } = await api.get<CityDto[] | PagedResult<CityDto>>('/cities', {
    params: { stateId }
  });
  return unwrap(data);
}
