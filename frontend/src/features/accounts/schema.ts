import { z } from 'zod';
import { AccountType } from './types';

/**
 * Account form schema (shared by create + edit). `code` and `type` are immutable after
 * creation, so the edit flow disables them but the schema still validates their presence.
 */
export const accountFormSchema = z.object({
  code: z
    .string()
    .trim()
    .min(1, 'accounts.validation.codeRequired')
    .max(20, 'accounts.validation.codeTooLong'),
  name: z
    .string()
    .trim()
    .min(1, 'accounts.validation.nameRequired')
    .max(200, 'accounts.validation.nameTooLong'),
  type: z.nativeEnum(AccountType),
  parentId: z.number().int().positive().nullable(),
  isActive: z.boolean()
});

/** The form's value shape. */
export type AccountFormValues = z.infer<typeof accountFormSchema>;
