/**
 * Wire contracts for the Currencies feature. These mirror the .NET
 * `Finance.ServiceModel.Nomenclature` records field-for-field (SDD-NOM-001 §2.0–§2.2)
 * — keep names identical so the JSON deserializes without remapping.
 */

/** Mirrors `Finance.ServiceModel.Nomenclature.CurrencyDto`. */
export interface CurrencyDto {
  id: number;
  /** ISO 4217 alphabetic code — three uppercase letters (e.g. "BGN", "EUR"). */
  isoCode: string;
  name: string;
  symbol?: string | null;
  isActive: boolean;
  /** Base64 rowversion token round-tripped on update for optimistic concurrency. */
  rowVersion: string;
}

/** Mirrors `Finance.ServiceModel.Nomenclature.CreateCurrencyRequest`. */
export interface CreateCurrencyRequest {
  isoCode: string;
  name: string;
  symbol?: string | null;
  isActive: boolean;
}

/**
 * Mirrors `Finance.ServiceModel.Nomenclature.UpdateCurrencyRequest`. Carries no `isoCode`
 * member — the path code is the sole authoritative source (SDD-NOM-001 §2.6).
 */
export interface UpdateCurrencyRequest {
  name: string;
  symbol?: string | null;
  isActive: boolean;
  rowVersion: string;
}

/**
 * Mirrors `Finance.ServiceModel.Nomenclature.ExchangeRateDto`. Reads are transactional
 * and never cached (SDD-NOM-001 §2.2).
 */
export interface ExchangeRateDto {
  currencyIsoCode: string;
  /** Six-decimal rate. */
  rate: number;
  /** Time-zone-aware ISO 8601 date the rate applies on. */
  rateDate: string;
}
