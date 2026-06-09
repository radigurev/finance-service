import { describe, it, expect } from 'vitest';
import { accountFormSchema } from './schema';
import { AccountType } from './types';

const valid = {
  code: '100',
  name: 'Cash',
  type: AccountType.Asset,
  parentId: null,
  isActive: true
};

describe('accountFormSchema', () => {
  it('accepts a valid account', () => {
    expect(accountFormSchema.safeParse(valid).success).toBe(true);
  });

  it('requires a code', () => {
    const result = accountFormSchema.safeParse({ ...valid, code: '   ' });
    expect(result.success).toBe(false);
    expect(messages(result)).toContain('accounts.validation.codeRequired');
  });

  it('rejects a code longer than 20 characters', () => {
    const result = accountFormSchema.safeParse({ ...valid, code: '1'.repeat(21) });
    expect(result.success).toBe(false);
    expect(messages(result)).toContain('accounts.validation.codeTooLong');
  });

  it('rejects a name longer than 200 characters', () => {
    const result = accountFormSchema.safeParse({ ...valid, name: 'x'.repeat(201) });
    expect(result.success).toBe(false);
    expect(messages(result)).toContain('accounts.validation.nameTooLong');
  });

  it('accepts a positive integer parent id', () => {
    expect(accountFormSchema.safeParse({ ...valid, parentId: 5 }).success).toBe(true);
  });
});

function messages(result: ReturnType<typeof accountFormSchema.safeParse>): string[] {
  return result.success ? [] : result.error.issues.map((i) => i.message);
}
