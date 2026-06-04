/**
 * Wire contracts for the Posting Rules + Posting Engine feature. These mirror the .NET
 * `Finance.ServiceModel.Posting` records field-for-field (SDD-FIN-006 §2.1, §2.5) — keep names
 * identical so the JSON deserializes without remapping.
 *
 * The Journal API registers no `JsonStringEnumConverter` (same as `JournalEntryStatus`), so the
 * `PostingDebitOrCredit` and `PostingAmountSource` enums travel the wire as their numeric values
 * matching the C# enum ordinals. The apply request's `Amounts` map is keyed by `PostingAmountSource`,
 * which System.Text.Json serializes as the numeric value rendered as a string (e.g. `"0"`, `"1"`, `"2"`).
 */

/** Mirrors `Finance.Country.Abstractions.PostingDebitOrCredit` (numeric on the wire). */
export enum PostingDebitOrCredit {
  Debit = 0,
  Credit = 1
}

/** Mirrors `Finance.Country.Abstractions.PostingAmountSource` (numeric on the wire). */
export enum PostingAmountSource {
  Net = 0,
  Tax = 1,
  Gross = 2
}

/** The amount sources offered in the line editor + apply dialog, in display order. */
export const AMOUNT_SOURCES: readonly PostingAmountSource[] = [
  PostingAmountSource.Net,
  PostingAmountSource.Tax,
  PostingAmountSource.Gross
];

/** The debit/credit sides offered in the line editor, in display order. */
export const DEBIT_OR_CREDIT: readonly PostingDebitOrCredit[] = [
  PostingDebitOrCredit.Debit,
  PostingDebitOrCredit.Credit
];

/** Maps a {@link PostingDebitOrCredit} to its i18n label key under `postingRules.side_*`. */
export function debitOrCreditLabelKey(value: PostingDebitOrCredit): string {
  return `postingRules.side_${PostingDebitOrCredit[value]}`;
}

/** Maps a {@link PostingAmountSource} to its i18n label key under `postingRules.source_*`. */
export function amountSourceLabelKey(value: PostingAmountSource): string {
  return `postingRules.source_${PostingAmountSource[value]}`;
}

/** Mirrors `Finance.ServiceModel.Posting.PostingRuleLineDto`. */
export interface PostingRuleLineDto {
  id: number;
  /** 1-based position of the line within the rule. */
  lineNumber: number;
  /** Chart-of-accounts code this line posts to (resolved to an account id at apply time). */
  accountSelector: string;
  debitOrCredit: PostingDebitOrCredit;
  amountSource: PostingAmountSource;
}

/** Mirrors `Finance.ServiceModel.Posting.PostingRuleDto`. */
export interface PostingRuleDto {
  id: number;
  /** Stable, unique, uppercase machine key (e.g. "SALE_INVOICE"). */
  ruleKey: string;
  description: string;
  /** ISO 3166-1 alpha-2 country code that owns the rule. */
  countryCode: string;
  isActive: boolean;
  lines: PostingRuleLineDto[];
  /** Base64 rowversion token round-tripped on update for optimistic concurrency. */
  rowVersion: string;
}

/** Mirrors `Finance.ServiceModel.Posting.CreatePostingRuleLineRequest` (shared by create + update). */
export interface CreatePostingRuleLineRequest {
  accountSelector: string;
  debitOrCredit: PostingDebitOrCredit;
  amountSource: PostingAmountSource;
}

/** Mirrors `Finance.ServiceModel.Posting.CreatePostingRuleRequest`. */
export interface CreatePostingRuleRequest {
  ruleKey: string;
  description: string;
  lines: CreatePostingRuleLineRequest[];
}

/**
 * Mirrors `Finance.ServiceModel.Posting.UpdatePostingRuleRequest`. Carries no `ruleKey` — it is
 * immutable after create (SDD-FIN-006 §2.1); the captured `rowVersion` is round-tripped for
 * optimistic concurrency (stale token → `CONCURRENT_MODIFICATION`).
 */
export interface UpdatePostingRuleRequest {
  description: string;
  isActive: boolean;
  lines: CreatePostingRuleLineRequest[];
  rowVersion: string;
}

/**
 * Mirrors `Finance.ServiceModel.Posting.ApplyPostingRuleRequest`. `amounts` is keyed by the numeric
 * `PostingAmountSource` value rendered as a string (System.Text.Json enum-key serialization). v1
 * SHOULD supply a base-currency context; multi-currency contexts are deferred to SDD-FIN-005.
 */
export interface ApplyPostingRuleRequest {
  ruleKey: string;
  amounts: Record<string, number>;
  currencyCode: string;
  /** ISO 8601 time-zone-aware accounting date. */
  entryDate: string;
  description?: string | null;
  accountOverrides?: Record<string, string> | null;
  /** When true (the default) the resulting draft is posted immediately; otherwise left as a draft. */
  postImmediately: boolean;
}
