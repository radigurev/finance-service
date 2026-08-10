/**
 * Wire contracts for the Payments feature (SDD-UI-FIN-002). These mirror the .NET
 * `Finance.ServiceModel.Payments` records field-for-field (SDD-PAY-001 §2.11, SDD-PAY-002 §2.7,
 * SDD-PAY-003 §2.5–§2.7) — keep names identical so the JSON deserializes without remapping.
 *
 * Enum serialization is MIXED on this one surface and it is the single easiest thing to get wrong
 * (SDD-UI-FIN-002 §1.2, §1.4 trap 1). `Finance.Payments.API` registers NO `JsonStringEnumConverter`,
 * so real C# enums travel as INTEGERS: `PaymentDto.documentType` / `.direction` / `.method` /
 * `.status`, `PaymentAllocationDto.invoiceSettlementStatus`, `AllocatedInvoiceSettlementDto` /
 * `OpenItemDto.settlementStatus`. Fields DECLARED as `string` on the DTO travel as STRINGS:
 * `OpenItemDto.documentType` / `.direction` / `.invoiceStatus` / `.agingBucket`,
 * `PaymentAllocationDto.invoiceStatus`, and every `direction` / `label` on the aging surfaces.
 * The two representations are modelled honestly below and are never silently normalized.
 *
 * `PaymentDirection` is `AP = 1`, `AR = 2` — deliberately NOT alphabetical-by-intuition; it mirrors
 * `InvoiceDirection` value-for-value (`src/Finance.Common/Enums/PaymentDirection.cs`). A TypeScript
 * enum that guesses `AR = 1` silently mislabels every row (§1.4 trap 1).
 */

import type { PagedResult } from '@/shared/api/paging';

/** Mirrors `Finance.Common.Enums.PaymentDocumentType` (NUMERIC on the wire). */
export enum PaymentDocumentType {
  CustomerReceipt = 1,
  SupplierPayment = 2
}

/** Mirrors `Finance.Common.Enums.PaymentDirection` (NUMERIC on the wire). `AP = 1`, `AR = 2`. */
export enum PaymentDirection {
  AP = 1,
  AR = 2
}

/** Mirrors `Finance.Common.Enums.PaymentMethod` (NUMERIC on the wire). */
export enum PaymentMethod {
  Cash = 1,
  BankTransfer = 2,
  Card = 3
}

/** Mirrors `Finance.Common.Enums.PaymentStatus` (NUMERIC on the wire). */
export enum PaymentStatus {
  Draft = 1,
  Confirmed = 2,
  Posted = 3,
  Cancelled = 4,
  Reversed = 5
}

/**
 * Mirrors `Finance.Common.Enums.SettlementStatus` (NUMERIC on the wire). Derived server-side by the
 * single `SettlementStatusCalculator` — the UI renders it and MUST NOT re-derive it from
 * `settledAmount` vs `grossTotal` (SDD-PAY-002 §2.8; SDD-UI-FIN-002 §2.10).
 */
export enum SettlementStatus {
  Unsettled = 1,
  PartiallySettled = 2,
  Settled = 3
}

/**
 * The STRING direction narrowing accepted by `OpenItemQueryRequest`, `AgingReportQueryRequest`, and
 * `CounterpartyBalanceQueryRequest` — never the numeric {@link PaymentDirection} (§1.2, §1.4 trap 1).
 */
export type AgingDirection = 'AR' | 'AP';

/** All selectable document types in declaration order. */
export const PAYMENT_DOCUMENT_TYPES: PaymentDocumentType[] = [
  PaymentDocumentType.CustomerReceipt,
  PaymentDocumentType.SupplierPayment
];

/** All selectable payment methods in declaration order. */
export const PAYMENT_METHODS: PaymentMethod[] = [
  PaymentMethod.Cash,
  PaymentMethod.BankTransfer,
  PaymentMethod.Card
];

/** The two string direction narrowings, in the order the selectors offer them. */
export const AGING_DIRECTIONS: AgingDirection[] = ['AR', 'AP'];

/** The server-generated bucket label that IS translated; every other label renders verbatim. */
export const CURRENT_BUCKET_LABEL = 'Current';

/**
 * The UI-only "posting…" affordance (SDD-UI-FIN-002 §2.7): a `Confirmed` payment whose
 * `journalEntryId` has not yet been linked by the Journal back-event. It is NOT a backend status
 * value and MUST NOT be added as one — it is derived for display only (see {@link displayStatusKey}).
 */
export const POSTING_PENDING = 'Posting' as const;

/** Maps a {@link PaymentDocumentType} to its i18n label key under `payments.type_*`. */
export function documentTypeLabelKey(type: PaymentDocumentType): string {
  return `payments.type_${PaymentDocumentType[type]}`;
}

/** Maps a numeric {@link PaymentDirection} to its i18n label key under `payments.direction_*`. */
export function directionLabelKey(direction: PaymentDirection): string {
  return `payments.direction_${PaymentDirection[direction]}`;
}

/**
 * Maps a STRING direction (`"AR"` / `"AP"`, as it arrives on `OpenItemDto`, `AgingReportDto`,
 * `AgingRowDto`, and `CounterpartyBalanceDto`) to its i18n label key under `payments.direction_*`.
 */
export function directionStringLabelKey(direction: string): string {
  return `payments.direction_${direction}`;
}

/** Maps a {@link PaymentMethod} to its i18n label key under `payments.method_*`. */
export function methodLabelKey(method: PaymentMethod): string {
  return `payments.method_${PaymentMethod[method]}`;
}

/** Maps a {@link SettlementStatus} to its i18n label key under `allocations.settlement_*`. */
export function settlementStatusLabelKey(status: SettlementStatus): string {
  return `allocations.settlement_${SettlementStatus[status]}`;
}

/**
 * Narrows the NUMERIC `PaymentDto.direction` into the STRING form the three read-surface query
 * contracts require (`"AR"` / `"AP"`). Used to pre-narrow the open-item picker (§2.11).
 */
export function agingDirectionOf(direction: PaymentDirection): AgingDirection {
  return PaymentDirection[direction] as AgingDirection;
}

/**
 * The direction the SERVER will derive from a document type (`CustomerReceipt → AR`,
 * `SupplierPayment → AP`). Shown read-only in the create form so the operator sees the consequence
 * of the type choice; it is NEVER sent — `CreatePaymentRequest` does not declare it (§2.3).
 */
export function derivedDirection(documentType: PaymentDocumentType): PaymentDirection {
  return documentType === PaymentDocumentType.CustomerReceipt
    ? PaymentDirection.AR
    : PaymentDirection.AP;
}

/**
 * Resolves the i18n status-label key for a payment, surfacing the posting-pending affordance: a
 * `Confirmed` payment with no linked `journalEntryId` renders as `payments.status_Posting`
 * ("posting…"); every other state maps to `payments.status_<StatusName>` (§2.7).
 */
export function displayStatusKey(payment: Pick<PaymentDto, 'status' | 'journalEntryId'>): string {
  if (payment.status === PaymentStatus.Confirmed && !payment.journalEntryId) {
    return `payments.status_${POSTING_PENDING}`;
  }
  return `payments.status_${PaymentStatus[payment.status]}`;
}

/** True while the Journal handshake has not linked an entry to a `Confirmed` payment (§2.7). */
export function isPostingPending(
  payment: Pick<PaymentDto, 'status' | 'journalEntryId'>
): boolean {
  return payment.status === PaymentStatus.Confirmed && !payment.journalEntryId;
}

/**
 * The document number as it must be DISPLAYED. `DocumentNumber` is NULL while `Draft` (it is drawn
 * from the gapless sequence inside the confirm transaction — §1.4 trap 4) and, because cancel is
 * `Draft`-ONLY, a `Cancelled` payment never held one and shows `—` FOREVER (§1.4 trap 5). This is
 * the OPPOSITE of SDD-UI-FIN-001 §2.7 for invoices; that rule MUST NOT be ported here.
 */
export function displayDocumentNumber(
  payment: Pick<PaymentDto, 'documentNumber'>
): string {
  return payment.documentNumber ?? '—';
}

/** Mirrors `Finance.ServiceModel.Payments.PaymentDto`. */
export interface PaymentDto {
  id: string;
  /** The gapless `RCT-…`/`PAY-…` number assigned at confirm; `null` while `Draft` (and `Cancelled`). */
  documentNumber: string | null;
  documentType: PaymentDocumentType;
  /** Server-derived and frozen; never sent on a request. */
  direction: PaymentDirection;
  method: PaymentMethod;
  status: PaymentStatus;
  counterpartyId: string;
  currencyCode: string;
  /** Server-owned base currency from the country strategy; response-only. */
  baseCurrencyCode: string;
  amount: number;
  /** `DECIMAL(18,6)` semantics. */
  exchangeRate: number;
  /** Server-computed and authoritative; the client preview is feedback only (§2.3). */
  baseAmount: number;
  settlementAccountId: number;
  paymentDate: string;
  bankReference: string | null;
  allocatedAmount: number;
  unallocatedAmount: number;
  /** The linked journal entry once posted; `null` until the posting handshake completes. */
  journalEntryId: string | null;
  cancellationReason: string | null;
  createdAt: string;
  confirmedAt: string | null;
  postedAt: string | null;
  reversedAt: string | null;
  /** Base64 rowversion round-tripped on every write for optimistic concurrency. */
  rowVersion: string;
}

/**
 * Mirrors `Finance.ServiceModel.Payments.PaymentAllocationDto`. Note the mixed shapes:
 * `invoiceStatus` is a STRING mirrored from the invoice projection while
 * `invoiceSettlementStatus` is the NUMERIC {@link SettlementStatus}.
 */
export interface PaymentAllocationDto {
  id: number;
  paymentId: string;
  invoiceId: string;
  allocatedAmount: number;
  /** Booking-rate figure, not a live revaluation. */
  baseAllocatedAmount: number;
  /** Informational only — `IRealizedFxHandler` is inert in v1 (SDD-PAY-002 §2.9). */
  realizedFxDifference: number;
  allocatedAt: string;
  invoiceDocumentNumber: string | null;
  invoiceDueDate: string | null;
  /** STRING on the wire (mirrored invoice status). */
  invoiceStatus: string | null;
  invoiceGrossTotal: number | null;
  /** NUMERIC on the wire; derived server-side. */
  invoiceSettlementStatus: SettlementStatus | null;
}

/**
 * Mirrors `Finance.ServiceModel.Payments.OpenItemDto`. `documentType`, `direction`, `agingBucket`,
 * and `invoiceStatus` are declared `string` on the DTO and therefore arrive as STRINGS, while
 * `settlementStatus` is the NUMERIC {@link SettlementStatus} (§1.4 trap 1).
 */
export interface OpenItemDto {
  invoiceId: string;
  documentNumber: string;
  /** STRING on the wire. */
  documentType: string;
  /** STRING on the wire (`"AR"` / `"AP"`). */
  direction: string;
  counterpartyId: string;
  currencyCode: string;
  baseCurrencyCode: string;
  grossTotal: number;
  settledAmount: number;
  outstanding: number;
  /** At the invoice's FROZEN booking rate — never a live conversion (§1.6 gap 19). */
  baseOutstanding: number;
  issueDate: string;
  dueDate: string;
  /** `≤ 0` means not yet due and corresponds to the `Current` bucket (§2.13). */
  daysPastDue: number;
  /** Server-generated label, not an i18n key (§1.4 trap 16). */
  agingBucket: string;
  /** NUMERIC on the wire. */
  settlementStatus: SettlementStatus;
  /** STRING on the wire — always `Confirmed` or `Posted`. */
  invoiceStatus: string;
}

/** Mirrors `Finance.ServiceModel.Payments.AgingBucketAmountDto` (per row, per bucket). */
export interface AgingBucketAmountDto {
  /** Server-generated data label (`"Current"`, `"1-30"`, …), NOT an i18n key. */
  label: string;
  fromDaysPastDue: number | null;
  toDaysPastDue: number | null;
  outstanding: number;
  baseOutstanding: number;
  itemCount: number;
}

/** Mirrors `Finance.ServiceModel.Payments.AgingBucketTotalDto` (report level, base currency only). */
export interface AgingBucketTotalDto {
  label: string;
  fromDaysPastDue: number | null;
  toDaysPastDue: number | null;
  baseOutstanding: number;
  itemCount: number;
}

/** Mirrors `Finance.ServiceModel.Payments.AgingRowDto` — one (counterparty, currency) pair. */
export interface AgingRowDto {
  counterpartyId: string;
  currencyCode: string;
  baseCurrencyCode: string;
  openItemCount: number;
  /** Aligned with `AgingReportDto.bucketLabels`; the column set is DYNAMIC. */
  buckets: AgingBucketAmountDto[];
  totalOutstanding: number;
  totalBaseOutstanding: number;
}

/**
 * Mirrors `Finance.ServiceModel.Payments.AgingReportDto`. The endpoint accepts NO `FilterRequest`
 * and returns EVERY in-scope row in one payload — there is no paging contract to invent
 * (§1.4 trap 15, §1.6 gap 8).
 */
export interface AgingReportDto {
  asOfDate: string;
  /** STRING on the wire. */
  direction: string;
  baseCurrencyCode: string;
  bucketDayBoundaries: number[];
  /** Drives the DYNAMIC bucket column set; never hard-code five columns (§1.4 trap 16). */
  bucketLabels: string[];
  rows: AgingRowDto[];
  /** Base-currency only, by design — no cross-currency transactional total exists (§2.14). */
  totals: AgingBucketTotalDto[];
  grandTotalBaseOutstanding: number;
  openItemCount: number;
}

/** Mirrors `Finance.ServiceModel.Payments.CounterpartyBalanceDto`. */
export interface CounterpartyBalanceDto {
  counterpartyId: string;
  currencyCode: string;
  baseCurrencyCode: string;
  /** STRING on the wire. */
  direction: string;
  openItemCount: number;
  outstanding: number;
  baseOutstanding: number;
  /** Exactly the sum of the non-`Current` buckets (§2.15). */
  overdueOutstanding: number;
  baseOverdueOutstanding: number;
  oldestDueDate: string | null;
}

/**
 * Mirrors `Finance.ServiceModel.Payments.CreatePaymentRequest`. `Direction`, `BaseCurrencyCode`,
 * and `BaseAmount` are deliberately ABSENT — all three are server-derived and a client value is
 * ignored (§2.3).
 */
export interface CreatePaymentRequest {
  documentType: PaymentDocumentType;
  method: PaymentMethod;
  counterpartyId: string;
  currencyCode: string;
  amount: number;
  exchangeRate: number;
  settlementAccountId: number;
  paymentDate: string;
  bankReference?: string | null;
}

/**
 * Mirrors `Finance.ServiceModel.Payments.UpdatePaymentRequest`. `documentType` is carried precisely
 * so the server can REJECT a change (it drives direction, the sequence key, and the posting rule):
 * it MUST be sent unchanged and rendered read-only in edit mode (§2.5).
 */
export interface UpdatePaymentRequest extends CreatePaymentRequest {
  rowVersion: string;
}

/** Mirrors `Finance.ServiceModel.Payments.ConfirmPaymentRequest`. */
export interface ConfirmPaymentRequest {
  rowVersion: string;
}

/** Mirrors `Finance.ServiceModel.Payments.PostPaymentRequest`. */
export interface PostPaymentRequest {
  rowVersion: string;
}

/** Mirrors `Finance.ServiceModel.Payments.CancelPaymentRequest` (`Draft` ONLY — §2.8). */
export interface CancelPaymentRequest {
  reason: string;
  rowVersion: string;
}

/** Mirrors `Finance.ServiceModel.Payments.ReversePaymentRequest` (`Posted` only — §2.9). */
export interface ReversePaymentRequest {
  reason: string;
  rowVersion: string;
}

/** Mirrors `Finance.ServiceModel.Payments.AllocatePaymentItem`. */
export interface AllocatePaymentItem {
  invoiceId: string;
  allocatedAmount: number;
}

/**
 * Mirrors `Finance.ServiceModel.Payments.AllocatePaymentRequest`. `items` is REQUIRED and an empty
 * list is never read as "apply the whole payment" (§1.6 gap 5).
 */
export interface AllocatePaymentRequest {
  items: AllocatePaymentItem[];
  rowVersion: string;
}

/** Mirrors `Finance.ServiceModel.Payments.AllocatedInvoiceSettlementDto`. */
export interface AllocatedInvoiceSettlementDto {
  invoiceId: string;
  settledAmount: number;
  /** NUMERIC on the wire. */
  settlementStatus: SettlementStatus;
}

/**
 * Mirrors `Finance.ServiceModel.Payments.AllocatePaymentResultDto`. Returned with **200, not 201**,
 * and with no `Location` header (§1.4 trap 10). It already carries the new `rowVersion`, the new
 * figures, and every affected invoice's post-change settlement state, so the caller MUST consume it
 * and MUST NOT issue a follow-up read (§2.11) — and MUST re-seed `rowVersion` from it (trap 11).
 */
export interface AllocatePaymentResultDto {
  paymentId: string;
  allocations: PaymentAllocationDto[];
  allocatedAmount: number;
  unallocatedAmount: number;
  rowVersion: string;
  affectedInvoices: AllocatedInvoiceSettlementDto[];
}

/** Mirrors `Finance.ServiceModel.Payments.DeallocatePaymentResultDto`. */
export interface DeallocatePaymentResultDto {
  paymentId: string;
  allocationId: number;
  invoiceId: string;
  releasedAmount: number;
  allocatedAmount: number;
  unallocatedAmount: number;
  rowVersion: string;
  affectedInvoice: AllocatedInvoiceSettlementDto;
}

/**
 * Mirrors `Finance.ServiceModel.Payments.OpenItemQueryRequest` — the narrowing record bound from the
 * SAME query string as the `FilterRequest` (§1.4 trap 8). Every field is OPTIONAL: `asOfDate`
 * defaults to today server-side and an omitted direction / counterparty / currency widens the list.
 */
export interface OpenItemQuery {
  asOfDate?: string;
  direction?: AgingDirection;
  counterpartyId?: string;
  currencyCode?: string;
  overdueOnly?: boolean;
}

/**
 * Mirrors `Finance.ServiceModel.Payments.AgingReportQueryRequest`. `asOfDate` and `direction` are
 * REQUIRED here; `buckets` is `int[]?` and binds ONLY as repeated query values
 * (`?buckets=30&buckets=60&buckets=90` — §1.4 trap 7).
 */
export interface AgingReportQuery {
  asOfDate: string;
  direction: AgingDirection;
  counterpartyId?: string;
  currencyCode?: string;
  buckets?: number[];
}

/**
 * Mirrors `Finance.ServiceModel.Payments.CounterpartyBalanceQueryRequest`. There is deliberately NO
 * counterparty narrowing (§1.6 gap 9) — single-counterparty detail is reached through `/open-items`.
 */
export interface CounterpartyBalanceQuery {
  asOfDate: string;
  direction: AgingDirection;
  currencyCode?: string;
}

/** Convenience aliases for the paged envelopes this feature consumes. */
export type PagedPayments = PagedResult<PaymentDto>;
/** Paged allocation rows for one payment. */
export type PagedAllocations = PagedResult<PaymentAllocationDto>;
/** Paged open items. */
export type PagedOpenItems = PagedResult<OpenItemDto>;
/** Paged counterparty balances. */
export type PagedCounterpartyBalances = PagedResult<CounterpartyBalanceDto>;
