import { describe, it, expect } from 'vitest';
import { en } from '@/shared/i18n/locales/en';
import { bg } from '@/shared/i18n/locales/bg';

type Dict = Record<string, unknown>;

/** Recursively collects dotted key paths from a nested locale object. */
function flattenKeys(obj: Dict, prefix = ''): string[] {
  return Object.entries(obj).flatMap(([key, value]) => {
    const path = prefix ? `${prefix}.${key}` : key;
    if (value !== null && typeof value === 'object' && !Array.isArray(value)) {
      return flattenKeys(value as Dict, path);
    }
    return [path];
  });
}

/**
 * The 43 backend payment error codes from `Finance.Common/ErrorCodes/PaymentErrorCodes.cs`
 * (SDD-PAY-001 §4, SDD-PAY-002 §4, SDD-PAY-003 §4) that MUST have `errors.<CODE>` entries in
 * BOTH locales per CLAUDE.md §0.3.B.
 */
const PAYMENT_ERROR_CODES: string[] = [
  'PAYMENT_NOT_FOUND',
  'PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND',
  'PAYMENT_SETTLEMENT_ACCOUNT_REQUIRED',
  'PAYMENT_SETTLEMENT_ACCOUNT_INACTIVE',
  'INVALID_PAYMENT_DOCUMENT_TYPE',
  'INVALID_PAYMENT_METHOD',
  'PAYMENT_COUNTERPARTY_REQUIRED',
  'INVALID_PAYMENT_CURRENCY',
  'INVALID_PAYMENT_AMOUNT',
  'INVALID_PAYMENT_EXCHANGE_RATE',
  'INVALID_PAYMENT_DATE',
  'INVALID_PAYMENT_BANK_REFERENCE',
  'PAYMENT_BASE_AMOUNT_MISMATCH',
  'PAYMENT_CANCEL_REASON_REQUIRED',
  'PAYMENT_REVERSE_REASON_REQUIRED',
  'PAYMENT_NOT_DRAFT',
  'PAYMENT_NOT_CONFIRMED',
  'PAYMENT_POSTING_PENDING',
  'PAYMENT_POSTED_IMMUTABLE',
  'INVALID_PAYMENT_STATE_TRANSITION',
  'PAYMENT_PERIOD_CLOSED',
  'PAYMENT_DUPLICATE_DOCUMENT_NUMBER',
  'PAYMENT_DATE_YEAR_MISMATCH',
  'PAYMENT_HAS_ALLOCATIONS',
  'PAYMENT_NOT_ALLOCATABLE',
  'PAYMENT_ALLOCATION_NOT_FOUND',
  'PAYMENT_ALLOCATION_INVOICE_NOT_FOUND',
  'PAYMENT_ALLOCATION_ITEMS_REQUIRED',
  'PAYMENT_ALLOCATION_INVOICE_REQUIRED',
  'INVALID_PAYMENT_ALLOCATION_AMOUNT',
  'PAYMENT_ALLOCATION_INVOICE_NOT_ELIGIBLE',
  'PAYMENT_ALLOCATION_EXCEEDS_PAYMENT',
  'PAYMENT_ALLOCATION_EXCEEDS_OUTSTANDING',
  'PAYMENT_ALLOCATION_DIRECTION_MISMATCH',
  'PAYMENT_ALLOCATION_COUNTERPARTY_MISMATCH',
  'PAYMENT_ALLOCATION_CURRENCY_MISMATCH',
  'PAYMENT_ALLOCATION_CONTROL_ACCOUNT_MISMATCH',
  'PAYMENT_ALLOCATION_DUPLICATE',
  'INVALID_AGING_AS_OF_DATE',
  'INVALID_AGING_DIRECTION',
  'INVALID_AGING_BUCKETS',
  'INVALID_COUNTERPARTY_ID',
  'INVALID_AGING_CURRENCY'
];

/**
 * The invoice-side code added by the SDD-INV-001 settlement amendment. Its sibling
 * `INVOICE_SETTLEMENT_EXCEEDS_GROSS_TOTAL` is deliberately absent: SDD-INV-001 §3.2/§3.3 give it no
 * HTTP surface, so it never reaches a client and needs no message.
 */
const SETTLEMENT_ERROR_CODES: string[] = ['INVOICE_HAS_SETTLEMENTS'];

describe('PaymentErrorCodes_I18n_AllCodesResolveInEnAndBg (SDD-PAY-001/002/003 §4)', () => {
  const enKeys = flattenKeys(en as unknown as Dict);
  const bgKeys = flattenKeys(bg as unknown as Dict);

  it.each([...PAYMENT_ERROR_CODES, ...SETTLEMENT_ERROR_CODES])(
    'defines errors.%s in EN',
    (code) => {
      expect(enKeys).toContain(`errors.${code}`);
    }
  );

  it.each([...PAYMENT_ERROR_CODES, ...SETTLEMENT_ERROR_CODES])(
    'defines errors.%s in BG',
    (code) => {
      expect(bgKeys).toContain(`errors.${code}`);
    }
  );

  it('resolves every code to a non-empty message in both locales', () => {
    const errorsEn = (en as unknown as Dict).errors as Record<string, string>;
    const errorsBg = (bg as unknown as Dict).errors as Record<string, string>;

    for (const code of [...PAYMENT_ERROR_CODES, ...SETTLEMENT_ERROR_CODES]) {
      expect(errorsEn[code]?.trim().length ?? 0).toBeGreaterThan(0);
      expect(errorsBg[code]?.trim().length ?? 0).toBeGreaterThan(0);
    }
  });

  it('never renders a raw key path as its own message', () => {
    const errorsEn = (en as unknown as Dict).errors as Record<string, string>;
    const errorsBg = (bg as unknown as Dict).errors as Record<string, string>;

    for (const code of [...PAYMENT_ERROR_CODES, ...SETTLEMENT_ERROR_CODES]) {
      expect(errorsEn[code]).not.toBe(code);
      expect(errorsBg[code]).not.toBe(code);
      expect(errorsEn[code]).not.toContain('errors.');
      expect(errorsBg[code]).not.toContain('errors.');
    }
  });

  it('keeps the whole errors group at exact EN/BG parity', () => {
    const enErrorKeys = enKeys.filter((k) => k.startsWith('errors.')).sort();
    const bgErrorKeys = bgKeys.filter((k) => k.startsWith('errors.')).sort();

    expect(bgErrorKeys).toEqual(enErrorKeys);
  });

  it('gives the BG messages Cyrillic text rather than untranslated English', () => {
    const errorsBg = (bg as unknown as Dict).errors as Record<string, string>;

    for (const code of [...PAYMENT_ERROR_CODES, ...SETTLEMENT_ERROR_CODES]) {
      expect(errorsBg[code]).toMatch(/[Ѐ-ӿ]/);
    }
  });
});
