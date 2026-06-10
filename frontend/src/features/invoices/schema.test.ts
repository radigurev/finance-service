import { describe, it, expect } from 'vitest';
import { invoiceFormSchema, previewLine, previewTotals } from './schema';
import { InvoiceDocumentType } from './types';

function line(over: Partial<Record<string, unknown>> = {}) {
  return { description: 'Widget', quantity: 2, unitPrice: 10, taxRate: 0.2, ...over };
}

function form(over: Partial<Record<string, unknown>> = {}) {
  return {
    documentType: InvoiceDocumentType.SaleInvoice,
    counterpartyId: '11111111-1111-1111-1111-111111111111',
    currencyCode: 'BGN',
    issueDate: '2026-06-01',
    dueDate: '2026-06-15',
    lines: [line()],
    ...over
  };
}

type ParseResultLike = { success: true } | { success: false; error: { issues: { message: string }[] } };

function issueMessages(result: ParseResultLike): string[] {
  return result.success ? [] : result.error.issues.map((i) => i.message);
}

describe('invoiceFormSchema (SDD-UI-FIN-001 §3)', () => {
  it('accepts a valid sale invoice with one line', () => {
    expect(invoiceFormSchema.safeParse(form()).success).toBe(true);
  });

  it('rejects a missing counterparty with the counterparty-required message', () => {
    const result = invoiceFormSchema.safeParse(form({ counterpartyId: '' }));
    expect(result.success).toBe(false);
    expect(issueMessages(result)).toContain('invoices.validation.counterpartyRequired');
  });

  it('rejects a due date before the issue date (cross-field)', () => {
    const result = invoiceFormSchema.safeParse(form({ issueDate: '2026-06-15', dueDate: '2026-06-01' }));
    expect(result.success).toBe(false);
    expect(issueMessages(result)).toContain('invoices.validation.dueDateBeforeIssue');
  });

  it('rejects an invoice with no lines', () => {
    const result = invoiceFormSchema.safeParse(form({ lines: [] }));
    expect(result.success).toBe(false);
    expect(issueMessages(result)).toContain('invoices.validation.minOneLine');
  });

  it('rejects a non-positive quantity', () => {
    const result = invoiceFormSchema.safeParse(form({ lines: [line({ quantity: 0 })] }));
    expect(result.success).toBe(false);
    expect(issueMessages(result)).toContain('invoices.validation.quantityPositive');
  });

  it('rejects a negative unit price', () => {
    const result = invoiceFormSchema.safeParse(form({ lines: [line({ unitPrice: -1 })] }));
    expect(result.success).toBe(false);
    expect(issueMessages(result)).toContain('invoices.validation.unitPriceNonNegative');
  });

  it('rejects a negative tax rate', () => {
    const result = invoiceFormSchema.safeParse(form({ lines: [line({ taxRate: -0.2 })] }));
    expect(result.success).toBe(false);
    expect(issueMessages(result)).toContain('invoices.validation.taxRateNonNegative');
  });

  it('rejects an invalid (non 3-char) currency code', () => {
    const result = invoiceFormSchema.safeParse(form({ currencyCode: 'EU' }));
    expect(result.success).toBe(false);
    expect(issueMessages(result)).toContain('invoices.validation.currencyRequired');
  });
});

describe('preview totals (SDD-UI-FIN-001 §2.4 — preview only, server authoritative)', () => {
  it('computes a single line net / tax / gross to money precision', () => {
    expect(previewLine({ quantity: 2, unitPrice: 10, taxRate: 0.2 })).toEqual({
      net: 20,
      tax: 4,
      gross: 24
    });
  });

  it('sums line previews into the header totals', () => {
    const totals = previewTotals([
      { quantity: 2, unitPrice: 10, taxRate: 0.2 },
      { quantity: 1, unitPrice: 5, taxRate: 0 }
    ]);
    expect(totals).toEqual({ net: 25, tax: 4, gross: 29 });
  });
});
