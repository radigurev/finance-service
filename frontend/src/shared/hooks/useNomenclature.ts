import { useCallback } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  fetchCurrencies,
  fetchCountries,
  fetchStates,
  fetchCities
} from '@/shared/nomenclature/api';
import type { CurrencyDto, CountryDto, StateDto, CityDto } from '@/shared/nomenclature/types';

interface UseNomenclature {
  /** Currency reference list (cached). */
  currencies: CurrencyDto[];
  /** Country reference list (cached). */
  countries: CountryDto[];
  /** True while either reference list is loading. */
  isLoading: boolean;
  /** Lazily loads (and caches) states for a country. */
  getStates: (countryCode: string) => Promise<StateDto[]>;
  /** Lazily loads (and caches) cities for a state. */
  getCities: (stateId: number) => Promise<CityDto[]>;
}

/**
 * Reference-data access hook (SDD-NOM-001 §2.4). Country / currency dropdowns load through
 * this hook rather than hard-coded option lists. `currencies` and `countries` are eagerly
 * fetched and cached for an hour (reference data); `getStates(countryIso)` and
 * `getCities(stateId)` are cascading, fetched on demand and memoized in the TanStack Query
 * cache. All four go through the shared axios instance (Bearer + X-Correlation-ID).
 */
export function useNomenclature(): UseNomenclature {
  const queryClient = useQueryClient();

  const currenciesQuery = useQuery({
    queryKey: ['nomenclature', 'currencies'],
    queryFn: fetchCurrencies,
    staleTime: 60 * 60 * 1000
  });

  const countriesQuery = useQuery({
    queryKey: ['nomenclature', 'countries'],
    queryFn: fetchCountries,
    staleTime: 60 * 60 * 1000
  });

  const getStates = useCallback(
    (countryCode: string) =>
      queryClient.fetchQuery({
        queryKey: ['nomenclature', 'states', countryCode],
        queryFn: () => fetchStates(countryCode),
        staleTime: 60 * 60 * 1000
      }),
    [queryClient]
  );

  const getCities = useCallback(
    (stateId: number) =>
      queryClient.fetchQuery({
        queryKey: ['nomenclature', 'cities', stateId],
        queryFn: () => fetchCities(stateId),
        staleTime: 60 * 60 * 1000
      }),
    [queryClient]
  );

  return {
    currencies: currenciesQuery.data ?? [],
    countries: countriesQuery.data ?? [],
    isLoading: currenciesQuery.isLoading || countriesQuery.isLoading,
    getStates,
    getCities
  };
}
