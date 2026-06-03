import { z } from 'zod';

/** The lowest fiscal year the generate dialog accepts. */
export const MIN_FISCAL_YEAR = 2000;

/** The highest fiscal year the generate dialog accepts. */
export const MAX_FISCAL_YEAR = 2100;

/**
 * Generate-periods form schema (SDD-FIN-004 §2.2). A single fiscal year within a sane bound;
 * the backend generates the 12 calendar-aligned monthly periods.
 */
export const generatePeriodsFormSchema = z.object({
  fiscalYear: z
    .number({ invalid_type_error: 'periods.validation.yearRequired' })
    .int('periods.validation.yearRequired')
    .min(MIN_FISCAL_YEAR, 'periods.validation.yearOutOfRange')
    .max(MAX_FISCAL_YEAR, 'periods.validation.yearOutOfRange')
});

/** The generate-periods form's value shape. */
export type GeneratePeriodsFormValues = z.infer<typeof generatePeriodsFormSchema>;

/**
 * Reason-prompt form schema shared by close / reopen (SDD-AUDIT-001 mandatory-reason list).
 */
export const reasonFormSchema = z.object({
  reason: z
    .string()
    .trim()
    .min(1, 'periods.validation.reasonRequired')
    .max(500, 'periods.validation.reasonTooLong')
});

/** The reason-prompt form's value shape. */
export type ReasonFormValues = z.infer<typeof reasonFormSchema>;
