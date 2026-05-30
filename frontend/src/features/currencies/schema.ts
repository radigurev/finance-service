import { z } from 'zod';

/**
 * Currency form schema (shared by create + edit). `isoCode` is immutable after creation,
 * so the edit flow disables it; the schema still validates its shape. The ISO-code rule
 * mirrors the backend `INVALID_CURRENCY_CODE` rule (exactly three uppercase letters).
 */
export const currencyFormSchema = z.object({
  isoCode: z
    .string()
    .trim()
    .regex(/^[A-Z]{3}$/, 'currencies.validation.isoCodeInvalid'),
  name: z
    .string()
    .trim()
    .min(1, 'currencies.validation.nameRequired')
    .max(100, 'currencies.validation.nameTooLong'),
  symbol: z
    .string()
    .trim()
    .max(5, 'currencies.validation.symbolTooLong'),
  isActive: z.boolean()
});

/** The form's value shape. */
export type CurrencyFormValues = z.infer<typeof currencyFormSchema>;
