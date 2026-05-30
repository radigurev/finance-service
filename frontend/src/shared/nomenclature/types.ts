/**
 * ISO currency reference item from the nomenclature service, normalized for dropdowns.
 * The Finance Nomenclature API serializes the code as `isoCode`; the proxy/normalizer
 * exposes it here as `code` so every dropdown consumer reads a single stable shape.
 */
export interface CurrencyDto {
  /** ISO 4217 alphabetic code (e.g. "BGN"). */
  code: string;
  name: string;
  symbol?: string | null;
}

/** Country reference item (proxied from Warehouse). */
export interface CountryDto {
  code: string;
  name: string;
}

/** State / province reference item. */
export interface StateDto {
  id: number;
  name: string;
  countryCode: string;
}

/** City reference item. */
export interface CityDto {
  id: number;
  name: string;
  stateId: number;
}
