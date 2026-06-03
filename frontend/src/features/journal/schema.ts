import { z } from 'zod';

/** Rounds to two-decimal money precision so floating drift does not break the balance check. */
function roundMoney(value: number): number {
  return Math.round(value * 100) / 100;
}

/**
 * A single journal-entry line. Exactly one of debit / credit may carry a positive amount
 * (debit XOR credit); the foreign rate must be positive; the base-currency amounts are the
 * pre-computed equivalents the engine reconciles (SDD-FIN-001 §2.4, §2.7).
 */
export const journalLineSchema = z
  .object({
    accountId: z
      .number({ invalid_type_error: 'journal.validation.accountRequired' })
      .int()
      .positive('journal.validation.accountRequired'),
    currencyCode: z.string().trim().min(1, 'journal.validation.currencyRequired'),
    exchangeRate: z
      .number({ invalid_type_error: 'journal.validation.ratePositive' })
      .positive('journal.validation.ratePositive'),
    debitAmount: z.number().min(0, 'journal.validation.amountNonNegative'),
    creditAmount: z.number().min(0, 'journal.validation.amountNonNegative'),
    description: z.string().trim().max(500, 'journal.validation.lineDescriptionTooLong').optional()
  })
  .superRefine((line, ctx) => {
    const hasDebit = line.debitAmount > 0;
    const hasCredit = line.creditAmount > 0;
    if (hasDebit && hasCredit) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'journal.validation.debitXorCredit',
        path: ['debitAmount']
      });
    }
    if (!hasDebit && !hasCredit) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'journal.validation.lineHasNoAmount',
        path: ['debitAmount']
      });
    }
  });

/**
 * Journal-entry form schema (shared by create + edit). Enforces a memo, an entry date, a
 * minimum of two lines, valid lines, and the double-entry balance invariant on the
 * base-currency amounts (SDD-FIN-001 §2.3, §2.5).
 */
export const journalFormSchema = z
  .object({
    entryDate: z.string().trim().min(1, 'journal.validation.entryDateRequired'),
    description: z
      .string()
      .trim()
      .min(1, 'journal.validation.descriptionRequired')
      .max(500, 'journal.validation.descriptionTooLong'),
    lines: z.array(journalLineSchema).min(2, 'journal.validation.minTwoLines')
  })
  .superRefine((form, ctx) => {
    const totalBaseDebit = roundMoney(
      form.lines.reduce((sum, line) => sum + baseAmount(line.debitAmount, line.exchangeRate), 0)
    );
    const totalBaseCredit = roundMoney(
      form.lines.reduce((sum, line) => sum + baseAmount(line.creditAmount, line.exchangeRate), 0)
    );
    if (totalBaseDebit !== totalBaseCredit) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'journal.validation.unbalanced',
        path: ['lines']
      });
    }
  });

/** Computes the base-currency equivalent of a transactional amount at a rate, to money precision. */
export function baseAmount(amount: number, exchangeRate: number): number {
  return roundMoney(amount * exchangeRate);
}

/** The form's value shape. */
export type JournalFormValues = z.infer<typeof journalFormSchema>;

/** A single line's form value shape. */
export type JournalLineFormValues = z.infer<typeof journalLineSchema>;
