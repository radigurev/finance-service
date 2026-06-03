/**
 * Wire contracts for the Fiscal Periods feature. These mirror the .NET
 * `Finance.ServiceModel.Periods` records field-for-field (SDD-FIN-004) — keep names identical
 * so the JSON deserializes without remapping.
 */

/**
 * Fiscal-period lifecycle state. The backend serializes `FiscalPeriodStatus` as its numeric
 * value (System.Text.Json default — no string-enum converter is registered for the Periods
 * API), so the wire contract for this field is an integer matching
 * `Finance.Common.Enums.FiscalPeriodStatus`.
 */
export enum FiscalPeriodStatus {
  Open = 1,
  Closed = 2
}

/** Maps a {@link FiscalPeriodStatus} to its i18n label key under `periods.status_*`. */
export function fiscalPeriodStatusLabelKey(status: FiscalPeriodStatus): string {
  return `periods.status_${FiscalPeriodStatus[status]}`;
}

/** Mirrors `Finance.ServiceModel.Periods.FiscalPeriodDto`. */
export interface FiscalPeriodDto {
  id: number;
  fiscalYear: number;
  periodNumber: number;
  name: string;
  /** ISO 8601 time-zone-aware start instant (inclusive). */
  startDate: string;
  /** ISO 8601 time-zone-aware end instant (inclusive). */
  endDate: string;
  status: FiscalPeriodStatus;
  closedAt: string | null;
  reopenedAt: string | null;
  /** Base64 rowversion token round-tripped on close/reopen for optimistic concurrency. */
  rowVersion: string;
}

/** Mirrors `Finance.ServiceModel.Periods.GeneratePeriodsRequest`. */
export interface GeneratePeriodsRequest {
  fiscalYear: number;
}

/** Mirrors `Finance.ServiceModel.Periods.CreatePeriodRequest`. */
export interface CreatePeriodRequest {
  fiscalYear: number;
  periodNumber: number;
  name?: string | null;
  startDate: string;
  endDate: string;
}

/** Mirrors `Finance.ServiceModel.Periods.ClosePeriodRequest`. */
export interface ClosePeriodRequest {
  reason: string;
  rowVersion: string;
}

/** Mirrors `Finance.ServiceModel.Periods.ReopenPeriodRequest`. */
export interface ReopenPeriodRequest {
  reason: string;
  rowVersion: string;
}
