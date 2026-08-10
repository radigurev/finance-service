import { z } from 'zod';
import { PaymentDocumentType, PaymentMethod, type AllocatePaymentItem } from './types';

/**
 * Client-side form schemas for the Payments feature (SDD-UI-FIN-002 §3). Each rule mirrors the
 * shipped FluentValidation shape so the operator gets immediate feedback, but the BACKEND remains
 * authoritative — every server validation error still surfaces via `getApiErrorMessage` (§4).
 * All validation messages are i18n keys, mirroring `features/invoices/schema.ts`.
 */

/** The maximum number of aging bucket boundaries the server accepts (`AgingBucketCalculator`). */
export const MAX_BUCKET_BOUNDARIES = 6;

/** The all-zero GUID; `Guid.Empty` is rejected by the backend for counterparty narrowings. */
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';

const GUID_PATTERN = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

const CURRENCY_PATTERN = /^[A-Z]{3}$/;

/**
 * Counts a number's decimal places EXACTLY. The server compares `DECIMAL(18,2)` / `DECIMAL(18,6)`
 * values with no tolerance, so the client pre-check uses the same exactness — a fraction of a cent
 * is blocked, never absorbed by an epsilon band (§2.18, §3.4).
 */
export function decimalPlaces(value: number): number {
  if (!Number.isFinite(value)) {
    return 0;
  }
  const text: string = Math.abs(value).toString();
  if (text.includes('e') || text.includes('E')) {
    const [mantissa, exponent] = text.split(/[eE]/);
    const mantissaDecimals: number = mantissa.includes('.')
      ? mantissa.split('.')[1].length
      : 0;
    return Math.max(0, mantissaDecimals - Number(exponent));
  }
  const dot: number = text.indexOf('.');
  return dot < 0 ? 0 : text.length - dot - 1;
}

/** True when `value` carries at most `scale` decimal places (exact, no epsilon). */
export function hasScale(value: number, scale: number): boolean {
  return decimalPlaces(value) <= scale;
}

/** Rounds to two-decimal money precision so floating drift does not break the preview. */
export function roundMoney(value: number): number {
  return Math.round(value * 100) / 100;
}

/**
 * Client PREVIEW of `baseAmount = round(amount × exchangeRate, 2)` (§2.3, §3.4). Feedback only: the
 * country strategy owns the rounding server-side, the persisted `PaymentDto.baseAmount` is
 * authoritative and is re-displayed after save, and this preview MUST NOT block submission.
 */
export function previewBaseAmount(amount: number, exchangeRate: number): number {
  return roundMoney((Number(amount) || 0) * (Number(exchangeRate) || 0));
}

/** Today as a `yyyy-MM-dd` calendar date, used for the whole-day "not in the future" comparisons. */
export function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}

/** True when a `yyyy-MM-dd` date string lies after today (whole-day granularity). */
export function isFutureDate(value: string): boolean {
  return value.slice(0, 10) > todayIso();
}

/**
 * Payment form schema, shared by create and edit. Mirrors `CreatePaymentRequestValidator` /
 * `UpdatePaymentRequestValidator` (§3.1). `direction`, `baseCurrencyCode`, and `baseAmount` are
 * absent by design — all three are server-derived and are never sent (§2.3).
 */
export const paymentFormSchema = z.object({
  documentType: z.nativeEnum(PaymentDocumentType, {
    errorMap: () => ({ message: 'payments.validation.documentTypeRequired' })
  }),
  method: z.nativeEnum(PaymentMethod, {
    errorMap: () => ({ message: 'payments.validation.methodRequired' })
  }),
  counterpartyId: z
    .string()
    .trim()
    .min(1, 'payments.validation.counterpartyRequired')
    .refine((value) => GUID_PATTERN.test(value), 'payments.validation.counterpartyRequired')
    .refine((value) => value.toLowerCase() !== EMPTY_GUID, 'payments.validation.counterpartyRequired'),
  currencyCode: z
    .string()
    .trim()
    .regex(CURRENCY_PATTERN, 'payments.validation.currencyRequired'),
  amount: z
    .number({ invalid_type_error: 'payments.validation.amountPositive' })
    .positive('payments.validation.amountPositive')
    .refine((value) => hasScale(value, 2), 'payments.validation.amountScale'),
  exchangeRate: z
    .number({ invalid_type_error: 'payments.validation.exchangeRatePositive' })
    .positive('payments.validation.exchangeRatePositive')
    .refine((value) => hasScale(value, 6), 'payments.validation.exchangeRateScale'),
  settlementAccountId: z
    .number({ invalid_type_error: 'payments.validation.settlementAccountRequired' })
    .int('payments.validation.settlementAccountRequired')
    .positive('payments.validation.settlementAccountRequired'),
  paymentDate: z
    .string()
    .trim()
    .min(1, 'payments.validation.paymentDateRequired')
    .refine((value) => !isFutureDate(value), 'payments.validation.paymentDateFuture'),
  bankReference: z
    .string()
    .trim()
    .max(64, 'payments.validation.bankReferenceTooLong')
});

/** The payment form's value shape. */
export type PaymentFormValues = z.infer<typeof paymentFormSchema>;

/** One allocation item as the picker holds it before submission. */
export interface AllocationDraftItem {
  invoiceId: string;
  allocatedAmount: number;
}

/** Cross-field context the allocate schema needs from the payment and the picked open items. */
export interface AllocateContext {
  /** `PaymentDto.unallocatedAmount` — the payment-side bound (server rule 8). */
  unallocatedAmount: number;
  /** `OpenItemDto.outstanding` per invoice id — the invoice-side bound (server rule 9). */
  outstandingByInvoice: Record<string, number>;
}

/**
 * Allocation form schema factory (§3.2, §3.4). Field rules mirror `AllocatePaymentItemValidator`;
 * the cross-field rules mirror server invariants 7 (no duplicate invoice within one request),
 * 8 (`Σ items ≤ unallocated`), and 9 (per invoice `amount ≤ outstanding`). Comparisons are EXACT
 * two-decimal — one cent over a bound fails, with no tolerance band (§2.18).
 */
export function allocateFormSchema(context: AllocateContext) {
  return z
    .object({
      items: z
        .array(
          z.object({
            invoiceId: z.string().trim().min(1, 'allocations.validation.invoiceRequired'),
            allocatedAmount: z
              .number({ invalid_type_error: 'allocations.validation.amountPositive' })
              .positive('allocations.validation.amountPositive')
              .refine((value) => hasScale(value, 2), 'allocations.validation.amountScale')
          })
        )
        .min(1, 'allocations.validation.itemsRequired')
    })
    .superRefine((form, ctx) => {
      const seen = new Set<string>();
      form.items.forEach((item, index) => {
        if (seen.has(item.invoiceId)) {
          ctx.addIssue({
            code: z.ZodIssueCode.custom,
            message: 'allocations.validation.duplicateInvoice',
            path: ['items', index, 'invoiceId']
          });
        }
        seen.add(item.invoiceId);

        const outstanding: number | undefined = context.outstandingByInvoice[item.invoiceId];
        if (outstanding !== undefined && roundMoney(item.allocatedAmount) > roundMoney(outstanding)) {
          ctx.addIssue({
            code: z.ZodIssueCode.custom,
            message: 'allocations.validation.exceedsOutstanding',
            path: ['items', index, 'allocatedAmount']
          });
        }
      });

      const total: number = roundMoney(
        form.items.reduce((sum, item) => sum + (Number(item.allocatedAmount) || 0), 0)
      );
      if (total > roundMoney(context.unallocatedAmount)) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: 'allocations.validation.exceedsUnallocated',
          path: ['items']
        });
      }
    });
}

/** Maps validated draft items onto the wire request items. */
export function toAllocationItems(items: AllocationDraftItem[]): AllocatePaymentItem[] {
  return items.map((item) => ({
    invoiceId: item.invoiceId,
    allocatedAmount: item.allocatedAmount
  }));
}

/**
 * Open-items query schema (§3.3). Every narrowing is OPTIONAL here — unlike the two aging surfaces,
 * where `asOfDate` and `direction` are required.
 */
export const openItemQuerySchema = z.object({
  asOfDate: z
    .string()
    .trim()
    .refine((value) => value === '' || !isFutureDate(value), 'aging.validation.asOfDateFuture'),
  direction: z.union([z.literal(''), z.literal('AR'), z.literal('AP')]),
  counterpartyId: z
    .string()
    .trim()
    .refine(
      (value) => value === '' || (GUID_PATTERN.test(value) && value.toLowerCase() !== EMPTY_GUID),
      'aging.validation.counterpartyInvalid'
    ),
  currencyCode: z
    .string()
    .trim()
    .refine((value) => value === '' || CURRENCY_PATTERN.test(value), 'aging.validation.currencyInvalid'),
  overdueOnly: z.boolean()
});

/** The open-items control-bar value shape. */
export type OpenItemQueryValues = z.infer<typeof openItemQuerySchema>;

/**
 * Validates a customized aging bucket boundary list against `AgingBucketCalculator.Validate`
 * (§3.3, §3.4): at most six values, each a strictly POSITIVE integer, strictly ASCENDING. Returns
 * the offending i18n message key, or `undefined` when the list is acceptable (an EMPTY list means
 * "not customized" and is always acceptable — the server then applies its documented default).
 */
export function validateBuckets(buckets: number[]): string | undefined {
  if (buckets.length === 0) {
    return undefined;
  }
  if (buckets.length > MAX_BUCKET_BOUNDARIES) {
    return 'aging.validation.bucketsTooMany';
  }
  if (buckets.some((boundary) => !Number.isInteger(boundary) || boundary <= 0)) {
    return 'aging.validation.bucketsPositive';
  }
  for (let index = 1; index < buckets.length; index += 1) {
    if (buckets[index] <= buckets[index - 1]) {
      return 'aging.validation.bucketsAscending';
    }
  }
  return undefined;
}

/**
 * Parses the operator's free-text bucket entry (e.g. `"30, 60, 90"`) into numbers. A blank entry
 * yields an EMPTY list, which the caller MUST translate into "omit the `buckets` param entirely"
 * so the server applies its default (§2.14). Non-numeric fragments become `NaN` so
 * {@link validateBuckets} rejects them via `bucketsPositive`.
 */
export function parseBuckets(text: string): number[] {
  return text
    .split(',')
    .map((part) => part.trim())
    .filter((part) => part !== '')
    .map((part) => Number(part));
}

/**
 * Aging-report query schema (§3.3). `asOfDate` and `direction` are REQUIRED here; the bucket list is
 * validated by {@link validateBuckets} because its three failure modes need distinct messages.
 */
export const agingQuerySchema = z.object({
  asOfDate: z
    .string()
    .trim()
    .min(1, 'aging.validation.asOfDateRequired')
    .refine((value) => !isFutureDate(value), 'aging.validation.asOfDateFuture'),
  direction: z.union([z.literal('AR'), z.literal('AP')], {
    errorMap: () => ({ message: 'aging.validation.directionRequired' })
  }),
  counterpartyId: z
    .string()
    .trim()
    .refine(
      (value) => value === '' || (GUID_PATTERN.test(value) && value.toLowerCase() !== EMPTY_GUID),
      'aging.validation.counterpartyInvalid'
    ),
  currencyCode: z
    .string()
    .trim()
    .refine((value) => value === '' || CURRENCY_PATTERN.test(value), 'aging.validation.currencyInvalid'),
  buckets: z.array(z.number()).superRefine((buckets, ctx) => {
    const message: string | undefined = validateBuckets(buckets);
    if (message) {
      ctx.addIssue({ code: z.ZodIssueCode.custom, message });
    }
  })
});

/** The aging control-bar value shape (shared with the counterparty-balances view). */
export type AgingQueryValues = z.infer<typeof agingQuerySchema>;

/**
 * Validates the shared aging/balances control bar for the BALANCES view, which requires only
 * `asOfDate` + `direction` + an optional `currencyCode` (§2.15, §3.3). Returns the offending i18n
 * message keys per field, empty when valid.
 */
export function validateBalancesQuery(values: {
  asOfDate: string;
  direction: string;
  currencyCode: string;
}): Partial<Record<'asOfDate' | 'direction' | 'currencyCode', string>> {
  const errors: Partial<Record<'asOfDate' | 'direction' | 'currencyCode', string>> = {};

  if (values.asOfDate.trim() === '') {
    errors.asOfDate = 'aging.validation.asOfDateRequired';
  } else if (isFutureDate(values.asOfDate)) {
    errors.asOfDate = 'aging.validation.asOfDateFuture';
  }

  if (values.direction !== 'AR' && values.direction !== 'AP') {
    errors.direction = 'aging.validation.directionRequired';
  }

  if (values.currencyCode.trim() !== '' && !CURRENCY_PATTERN.test(values.currencyCode.trim())) {
    errors.currencyCode = 'aging.validation.currencyInvalid';
  }

  return errors;
}

/**
 * Validates the shared aging/balances control bar for the AGING view — the balances rules plus the
 * optional counterparty narrowing and the bucket boundaries (§2.14, §3.3).
 */
export function validateAgingQuery(values: {
  asOfDate: string;
  direction: string;
  counterpartyId: string;
  currencyCode: string;
  buckets: number[];
}): Partial<Record<'asOfDate' | 'direction' | 'counterpartyId' | 'currencyCode' | 'buckets', string>> {
  const errors: Partial<
    Record<'asOfDate' | 'direction' | 'counterpartyId' | 'currencyCode' | 'buckets', string>
  > = validateBalancesQuery(values);

  const counterparty: string = values.counterpartyId.trim();
  if (
    counterparty !== '' &&
    (!GUID_PATTERN.test(counterparty) || counterparty.toLowerCase() === EMPTY_GUID)
  ) {
    errors.counterpartyId = 'aging.validation.counterpartyInvalid';
  }

  const bucketMessage: string | undefined = validateBuckets(values.buckets);
  if (bucketMessage) {
    errors.buckets = bucketMessage;
  }

  return errors;
}

/**
 * Validates the open-items control bar (§3.3). Everything is optional; only a FUTURE `asOfDate`, a
 * malformed counterparty GUID, or a malformed currency code is rejected.
 */
export function validateOpenItemQuery(values: {
  asOfDate: string;
  counterpartyId: string;
  currencyCode: string;
}): Partial<Record<'asOfDate' | 'counterpartyId' | 'currencyCode', string>> {
  const errors: Partial<Record<'asOfDate' | 'counterpartyId' | 'currencyCode', string>> = {};

  if (values.asOfDate.trim() !== '' && isFutureDate(values.asOfDate)) {
    errors.asOfDate = 'aging.validation.asOfDateFuture';
  }

  const counterparty: string = values.counterpartyId.trim();
  if (
    counterparty !== '' &&
    (!GUID_PATTERN.test(counterparty) || counterparty.toLowerCase() === EMPTY_GUID)
  ) {
    errors.counterpartyId = 'aging.validation.counterpartyInvalid';
  }

  if (values.currencyCode.trim() !== '' && !CURRENCY_PATTERN.test(values.currencyCode.trim())) {
    errors.currencyCode = 'aging.validation.currencyInvalid';
  }

  return errors;
}
