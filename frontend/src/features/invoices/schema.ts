import { z } from 'zod';
import { InvoiceDocumentType } from './types';

/** Rounds to two-decimal money precision so floating drift does not break the preview totals. */
function roundMoney(value: number): number {
  return Math.round(value * 100) / 100;
}

/**
 * A single invoice line (SDD-UI-FIN-001 §3.1; mirrors SDD-INV-001 §2.8). The operator supplies
 * the description, quantity, unit price, and tax rate; the server computes the authoritative
 * net / tax / gross via the country strategy. The validation messages are i18n keys.
 */
export const invoiceLineSchema = z.object({
  description: z
    .string()
    .trim()
    .min(1, 'invoices.validation.lineDescriptionRequired')
    .max(500, 'invoices.validation.lineDescriptionTooLong'),
  quantity: z
    .number({ invalid_type_error: 'invoices.validation.quantityPositive' })
    .positive('invoices.validation.quantityPositive'),
  unitPrice: z
    .number({ invalid_type_error: 'invoices.validation.unitPriceNonNegative' })
    .min(0, 'invoices.validation.unitPriceNonNegative'),
  taxRate: z
    .number({ invalid_type_error: 'invoices.validation.taxRateNonNegative' })
    .min(0, 'invoices.validation.taxRateNonNegative')
});

/**
 * Invoice form schema (shared by create + edit). Mirrors the backend shape (SDD-INV-001 §3.1)
 * so the operator gets immediate feedback; the server remains authoritative. Enforces a document
 * type, a counterparty, a 3-char ISO currency, issue/due dates with `dueDate ≥ issueDate`, at
 * least one line, and per-line quantity / unit-price / tax-rate rules (SDD-UI-FIN-001 §3).
 */
export const invoiceFormSchema = z
  .object({
    documentType: z.nativeEnum(InvoiceDocumentType, {
      errorMap: () => ({ message: 'invoices.validation.documentTypeRequired' })
    }),
    counterpartyId: z.string().trim().min(1, 'invoices.validation.counterpartyRequired'),
    currencyCode: z
      .string()
      .trim()
      .length(3, 'invoices.validation.currencyRequired'),
    issueDate: z.string().trim().min(1, 'invoices.validation.issueDateRequired'),
    dueDate: z.string().trim().min(1, 'invoices.validation.dueDateRequired'),
    lines: z.array(invoiceLineSchema).min(1, 'invoices.validation.minOneLine')
  })
  .superRefine((form, ctx) => {
    if (form.issueDate && form.dueDate && form.dueDate < form.issueDate) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'invoices.validation.dueDateBeforeIssue',
        path: ['dueDate']
      });
    }
  });

/** The running totals previewed beneath the line editor (server totals override after save). */
export interface PreviewTotals {
  net: number;
  tax: number;
  gross: number;
}

/** Computed line components used for the client preview (server recomputes authoritatively). */
export interface LinePreview extends PreviewTotals {}

/**
 * Client-side PREVIEW of a single line's net / tax / gross from quantity, unit price, and tax
 * rate (SDD-UI-FIN-001 §2.4, §3.2): `net = round(qty × unitPrice)`, `tax = round(net × rate)`,
 * `gross = net + tax`. This is feedback only — the country strategy rounding is server-side and
 * the persisted server values are re-displayed after save.
 */
export function previewLine(line: {
  quantity: number;
  unitPrice: number;
  taxRate: number;
}): LinePreview {
  const net = roundMoney((Number(line.quantity) || 0) * (Number(line.unitPrice) || 0));
  const tax = roundMoney(net * (Number(line.taxRate) || 0));
  return { net, tax, gross: roundMoney(net + tax) };
}

/** Sums the per-line previews into the header preview totals (`Σ net`, `Σ tax`, `Σ gross`). */
export function previewTotals(
  lines: { quantity: number; unitPrice: number; taxRate: number }[]
): PreviewTotals {
  return (lines ?? []).reduce<PreviewTotals>(
    (acc, line) => {
      const linePreview = previewLine(line);
      return {
        net: roundMoney(acc.net + linePreview.net),
        tax: roundMoney(acc.tax + linePreview.tax),
        gross: roundMoney(acc.gross + linePreview.gross)
      };
    },
    { net: 0, tax: 0, gross: 0 }
  );
}

/** The form's value shape. */
export type InvoiceFormValues = z.infer<typeof invoiceFormSchema>;

/** A single line's form value shape. */
export type InvoiceLineFormValues = z.infer<typeof invoiceLineSchema>;
