import { z } from 'zod';
import { PostingAmountSource, PostingDebitOrCredit } from './types';

/**
 * A single posting-rule line. Mirrors the backend shape rules (SDD-FIN-006 §3.1): a non-empty
 * account selector and valid debit/credit + amount-source enum values. Numeric balance is enforced
 * at apply time, not here; the form only checks structural balanceability (≥1 debit AND ≥1 credit).
 */
export const postingRuleLineSchema = z.object({
  accountSelector: z
    .string()
    .trim()
    .min(1, 'postingRules.validation.accountRequired')
    .max(20, 'postingRules.validation.accountTooLong'),
  debitOrCredit: z.nativeEnum(PostingDebitOrCredit),
  amountSource: z.nativeEnum(PostingAmountSource)
});

/**
 * Posting-rule form schema (shared by create + edit). Enforces an uppercase machine key, a
 * description, at least one line, and structural balanceability — at least one debit AND one
 * credit line (SDD-FIN-006 §2.1, §3.2). `isActive` is edit-only (a new rule is always active).
 */
export const postingRuleFormSchema = z
  .object({
    ruleKey: z
      .string()
      .trim()
      .min(1, 'postingRules.validation.ruleKeyRequired')
      .max(50, 'postingRules.validation.ruleKeyTooLong')
      .regex(/^[A-Z0-9_]+$/, 'postingRules.validation.ruleKeyInvalid'),
    description: z
      .string()
      .trim()
      .min(1, 'postingRules.validation.descriptionRequired')
      .max(500, 'postingRules.validation.descriptionTooLong'),
    isActive: z.boolean(),
    lines: z.array(postingRuleLineSchema).min(1, 'postingRules.validation.minOneLine')
  })
  .superRefine((form, ctx) => {
    const hasDebit = form.lines.some((line) => line.debitOrCredit === PostingDebitOrCredit.Debit);
    const hasCredit = form.lines.some((line) => line.debitOrCredit === PostingDebitOrCredit.Credit);
    if (!hasDebit || !hasCredit) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'postingRules.validation.notBalanceable',
        path: ['lines']
      });
    }
  });

/** The posting-rule form's value shape. */
export type PostingRuleFormValues = z.infer<typeof postingRuleFormSchema>;

/** A single line's form value shape. */
export type PostingRuleLineFormValues = z.infer<typeof postingRuleLineSchema>;

/**
 * Apply form schema (SDD-FIN-006 §2.5). The caller supplies the named monetary amounts (non-negative),
 * a currency, an entry date, and whether to post the resulting entry immediately. The engine validates
 * which sources the resolved rule actually requires; the form only ensures finite, non-negative values.
 */
export const applyPostingRuleFormSchema = z.object({
  net: z.number({ invalid_type_error: 'postingRules.validation.amountNonNegative' }).min(0, 'postingRules.validation.amountNonNegative'),
  tax: z.number({ invalid_type_error: 'postingRules.validation.amountNonNegative' }).min(0, 'postingRules.validation.amountNonNegative'),
  gross: z.number({ invalid_type_error: 'postingRules.validation.amountNonNegative' }).min(0, 'postingRules.validation.amountNonNegative'),
  currencyCode: z.string().trim().min(1, 'postingRules.validation.currencyRequired'),
  entryDate: z.string().trim().min(1, 'postingRules.validation.entryDateRequired'),
  postImmediately: z.boolean()
});

/** The apply form's value shape. */
export type ApplyPostingRuleFormValues = z.infer<typeof applyPostingRuleFormSchema>;
