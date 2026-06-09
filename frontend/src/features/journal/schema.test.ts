import { describe, it, expect } from 'vitest';
import { journalFormSchema, journalLineSchema, baseAmount } from './schema';

function debitLine(over: Partial<Record<string, unknown>> = {}) {
  return { accountId: 1, currencyCode: 'BGN', exchangeRate: 1, debitAmount: 100, creditAmount: 0, ...over };
}
function creditLine(over: Partial<Record<string, unknown>> = {}) {
  return { accountId: 2, currencyCode: 'BGN', exchangeRate: 1, debitAmount: 0, creditAmount: 100, ...over };
}

describe('journalLineSchema (SDD-FIN-001)', () => {
  it('accepts a debit-only line', () => {
    expect(journalLineSchema.safeParse(debitLine()).success).toBe(true);
  });

  it('rejects a line that has both debit and credit (debit XOR credit)', () => {
    const result = journalLineSchema.safeParse(debitLine({ creditAmount: 50 }));
    expect(result.success).toBe(false);
    expect(issueMessages(result)).toContain('journal.validation.debitXorCredit');
  });

  it('rejects a line that has neither debit nor credit', () => {
    const result = journalLineSchema.safeParse(debitLine({ debitAmount: 0, creditAmount: 0 }));
    expect(result.success).toBe(false);
    expect(issueMessages(result)).toContain('journal.validation.lineHasNoAmount');
  });

  it('rejects a non-positive exchange rate', () => {
    const result = journalLineSchema.safeParse(debitLine({ exchangeRate: 0 }));
    expect(result.success).toBe(false);
    expect(issueMessages(result)).toContain('journal.validation.ratePositive');
  });
});

describe('journalFormSchema (SDD-FIN-001 balance invariant)', () => {
  it('accepts a balanced two-line entry', () => {
    const result = journalFormSchema.safeParse({
      entryDate: '2026-06-01',
      description: 'Opening balance',
      lines: [debitLine(), creditLine()]
    });
    expect(result.success).toBe(true);
  });

  it('rejects fewer than two lines', () => {
    const result = journalFormSchema.safeParse({
      entryDate: '2026-06-01',
      description: 'x',
      lines: [debitLine()]
    });
    expect(result.success).toBe(false);
    expect(issueMessages(result)).toContain('journal.validation.minTwoLines');
  });

  it('rejects an unbalanced entry on the base-currency totals', () => {
    const result = journalFormSchema.safeParse({
      entryDate: '2026-06-01',
      description: 'Lopsided',
      lines: [debitLine({ debitAmount: 100 }), creditLine({ creditAmount: 80 })]
    });
    expect(result.success).toBe(false);
    expect(issueMessages(result)).toContain('journal.validation.unbalanced');
  });

  it('balances using the foreign rate so the base amounts reconcile', () => {
    // 50 EUR @ 1.95583 debit balances 97.79 BGN credit at rate 1.
    const result = journalFormSchema.safeParse({
      entryDate: '2026-06-01',
      description: 'FX',
      lines: [
        debitLine({ currencyCode: 'EUR', exchangeRate: 1.95583, debitAmount: 50 }),
        creditLine({ currencyCode: 'BGN', exchangeRate: 1, creditAmount: 97.79 })
      ]
    });
    expect(result.success).toBe(true);
  });
});

describe('baseAmount', () => {
  it('rounds to two-decimal money precision', () => {
    expect(baseAmount(50, 1.95583)).toBe(97.79);
  });
});

type ParseResultLike = { success: true } | { success: false; error: { issues: { message: string }[] } };

function issueMessages(result: ParseResultLike): string[] {
  return result.success ? [] : result.error.issues.map((i) => i.message);
}
