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

/** The 17 backend invoice error codes (SDD-INV-001 §4) that MUST have `errors.<CODE>` entries. */
const INVOICE_ERROR_CODES: string[] = [
  'INVOICE_NOT_FOUND',
  'INVOICE_LINES_REQUIRED',
  'INVALID_INVOICE_DOCUMENT_TYPE',
  'INVOICE_COUNTERPARTY_REQUIRED',
  'INVALID_INVOICE_CURRENCY',
  'INVALID_INVOICE_DATE',
  'INVALID_INVOICE_DUE_DATE',
  'INVALID_INVOICE_LINE',
  'INVALID_INVOICE_TAX_RATE',
  'INVOICE_TOTALS_MISMATCH',
  'INVOICE_NOT_DRAFT',
  'INVOICE_NOT_CONFIRMED',
  'INVOICE_POSTED_IMMUTABLE',
  'INVALID_INVOICE_STATE_TRANSITION',
  'INVOICE_PERIOD_CLOSED',
  'INVOICE_DUPLICATE_DOCUMENT_NUMBER',
  'INVOICE_CANCEL_REASON_REQUIRED'
];

describe('Invoices_I18n_AllKeysExistInEnAndBg (SDD-UI-FIN-001 §5)', () => {
  const enKeys = flattenKeys(en as unknown as Dict);
  const bgKeys = flattenKeys(bg as unknown as Dict);
  const enInvoiceKeys = enKeys.filter((k) => k.startsWith('invoices.')).sort();
  const bgInvoiceKeys = bgKeys.filter((k) => k.startsWith('invoices.')).sort();

  it('defines at least the documented invoices key groups in EN', () => {
    expect(enInvoiceKeys.length).toBeGreaterThan(40);
  });

  it('keeps every invoices.* key in strict EN/BG parity', () => {
    expect(enInvoiceKeys).toEqual(bgInvoiceKeys);
  });

  it('exposes the navigation entry in both locales', () => {
    expect(enKeys).toContain('nav.invoices');
    expect(bgKeys).toContain('nav.invoices');
  });

  it.each(INVOICE_ERROR_CODES)('maps the %s error code in both locales', (code) => {
    expect(enKeys).toContain(`errors.${code}`);
    expect(bgKeys).toContain(`errors.${code}`);
  });

  it('keeps the CONCURRENT_MODIFICATION and GENERIC_ERROR fallbacks in both locales', () => {
    expect(enKeys).toContain('errors.CONCURRENT_MODIFICATION');
    expect(bgKeys).toContain('errors.CONCURRENT_MODIFICATION');
    expect(enKeys).toContain('errors.GENERIC_ERROR');
    expect(bgKeys).toContain('errors.GENERIC_ERROR');
  });
});
