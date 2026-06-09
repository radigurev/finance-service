import { describe, it, expect } from 'vitest';
import { currencyFormSchema } from './schema';

const valid = { isoCode: 'BGN', name: 'Bulgarian Lev', symbol: 'лв', isActive: true };

describe('currencyFormSchema', () => {
  it('accepts a valid currency', () => {
    expect(currencyFormSchema.safeParse(valid).success).toBe(true);
  });

  it('rejects an ISO code that is not three uppercase letters (mirrors INVALID_CURRENCY_CODE)', () => {
    for (const bad of ['bg', 'BG', 'BGNN', 'B1N']) {
      const result = currencyFormSchema.safeParse({ ...valid, isoCode: bad });
      expect(result.success, `expected ${bad} to be rejected`).toBe(false);
      expect(messages(result)).toContain('currencies.validation.isoCodeInvalid');
    }
  });

  it('requires a name', () => {
    const result = currencyFormSchema.safeParse({ ...valid, name: '  ' });
    expect(result.success).toBe(false);
    expect(messages(result)).toContain('currencies.validation.nameRequired');
  });

  it('rejects a symbol longer than five characters', () => {
    const result = currencyFormSchema.safeParse({ ...valid, symbol: 'toolong' });
    expect(result.success).toBe(false);
    expect(messages(result)).toContain('currencies.validation.symbolTooLong');
  });
});

function messages(result: ReturnType<typeof currencyFormSchema.safeParse>): string[] {
  return result.success ? [] : result.error.issues.map((i) => i.message);
}
