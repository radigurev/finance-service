import { describe, it, expect } from 'vitest';
import {
  generatePeriodsFormSchema,
  reasonFormSchema,
  MIN_FISCAL_YEAR,
  MAX_FISCAL_YEAR
} from './schema';

describe('generatePeriodsFormSchema (SDD-FIN-004)', () => {
  it('accepts an in-range fiscal year', () => {
    expect(generatePeriodsFormSchema.safeParse({ fiscalYear: 2026 }).success).toBe(true);
  });

  it('rejects a year below the minimum', () => {
    const result = generatePeriodsFormSchema.safeParse({ fiscalYear: MIN_FISCAL_YEAR - 1 });
    expect(result.success).toBe(false);
    expect(messages(result)).toContain('periods.validation.yearOutOfRange');
  });

  it('rejects a year above the maximum', () => {
    const result = generatePeriodsFormSchema.safeParse({ fiscalYear: MAX_FISCAL_YEAR + 1 });
    expect(result.success).toBe(false);
    expect(messages(result)).toContain('periods.validation.yearOutOfRange');
  });

  it('rejects a non-integer year', () => {
    expect(generatePeriodsFormSchema.safeParse({ fiscalYear: 2026.5 }).success).toBe(false);
  });
});

describe('reasonFormSchema (SDD-AUDIT-001 mandatory reason)', () => {
  it('requires a non-empty reason for sensitive close/reopen ops', () => {
    const result = reasonFormSchema.safeParse({ reason: '   ' });
    expect(result.success).toBe(false);
    expect(messages(result)).toContain('periods.validation.reasonRequired');
  });

  it('rejects a reason longer than 500 characters', () => {
    const result = reasonFormSchema.safeParse({ reason: 'x'.repeat(501) });
    expect(result.success).toBe(false);
    expect(messages(result)).toContain('periods.validation.reasonTooLong');
  });

  it('accepts a valid reason', () => {
    expect(reasonFormSchema.safeParse({ reason: 'Year-end close' }).success).toBe(true);
  });
});

function messages(
  result:
    | ReturnType<typeof generatePeriodsFormSchema.safeParse>
    | ReturnType<typeof reasonFormSchema.safeParse>
): string[] {
  return result.success ? [] : result.error.issues.map((i) => i.message);
}
