/**
 * Wire contracts for the Journal feature. These mirror the .NET
 * `Finance.ServiceModel.Journal` records field-for-field (SDD-FIN-001, SDD-FIN-002) —
 * keep names identical so the JSON deserializes without remapping.
 */

/**
 * Journal-entry lifecycle state. The backend serializes `JournalEntryStatus` as its numeric
 * value (System.Text.Json default — no string-enum converter is registered for the Journal
 * API), so the wire contract for this field is an integer matching
 * `Finance.Common.Enums.JournalEntryStatus`.
 */
export enum JournalEntryStatus {
  Draft = 1,
  Posted = 2,
  Reversed = 3
}

/** Maps a {@link JournalEntryStatus} to its i18n label key under `journal.status_*`. */
export function journalStatusLabelKey(status: JournalEntryStatus): string {
  return `journal.status_${JournalEntryStatus[status]}`;
}

/** Mirrors `Finance.ServiceModel.Journal.JournalEntryLineDto`. */
export interface JournalEntryLineDto {
  id: number;
  accountId: number;
  debitAmount: number;
  creditAmount: number;
  currencyCode: string;
  exchangeRate: number;
  baseDebitAmount: number;
  baseCreditAmount: number;
  lineNumber: number;
  description?: string | null;
}

/** Mirrors `Finance.ServiceModel.Journal.JournalEntryDto`. */
export interface JournalEntryDto {
  id: string;
  /** The gapless document number assigned at posting; `null` while `Draft`. */
  entryNumber: string | null;
  /** ISO 8601 time-zone-aware accounting date. */
  entryDate: string;
  description: string;
  baseCurrencyCode: string;
  status: JournalEntryStatus;
  /** On a reversal entry, the id of the original entry it reverses; otherwise `null`. */
  reversesEntryId: string | null;
  createdAt: string;
  postedAt: string | null;
  lines: JournalEntryLineDto[];
  /** Base64 rowversion token round-tripped on update/post/reverse for optimistic concurrency. */
  rowVersion: string;
}

/** Mirrors `Finance.ServiceModel.Journal.JournalEntryLineRequest`. */
export interface JournalEntryLineRequest {
  accountId: number;
  debitAmount: number;
  creditAmount: number;
  currencyCode: string;
  exchangeRate: number;
  baseDebitAmount: number;
  baseCreditAmount: number;
  description?: string | null;
}

/**
 * Mirrors `Finance.ServiceModel.Journal.CreateJournalEntryRequest`. The base currency is
 * sourced server-side from configuration and is NOT part of the request body.
 */
export interface CreateJournalEntryRequest {
  entryDate: string;
  description: string;
  lines: JournalEntryLineRequest[];
}

/** Mirrors `Finance.ServiceModel.Journal.UpdateJournalEntryRequest`. */
export interface UpdateJournalEntryRequest {
  entryDate: string;
  description: string;
  lines: JournalEntryLineRequest[];
  rowVersion: string;
}

/** Mirrors `Finance.ServiceModel.Journal.PostJournalEntryRequest`. */
export interface PostJournalEntryRequest {
  rowVersion: string;
}

/** Mirrors `Finance.ServiceModel.Journal.ReverseJournalEntryRequest`. */
export interface ReverseJournalEntryRequest {
  reason: string;
  rowVersion: string;
}
