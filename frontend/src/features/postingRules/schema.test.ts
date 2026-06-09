import { describe, it, expect } from 'vitest';
import { postingRuleFormSchema, applyPostingRuleFormSchema } from './schema';
import { PostingAmountSource, PostingDebitOrCredit } from './types';

function debitLine() {
  return { accountSelector: '411', debitOrCredit: PostingDebitOrCredit.Debit, amountSource: PostingAmountSource.Gross };
}
function creditLine() {
  return { accountSelector: '702', debitOrCredit: PostingDebitOrCredit.Credit, amountSource: PostingAmountSource.Net };
}

describe('postingRuleFormSchema (SDD-FIN-006)', () => {
  it('accepts a balanceable rule with one debit and one credit line', () => {
    const result = postingRuleFormSchema.safeParse({
      ruleKey: 'SALE_INVOICE',
      description: 'Sale invoice posting',
      isActive: true,
      lines: [debitLine(), creditLine()]
    });
    expect(result.success).toBe(true);
  });

  it('rejects a rule key that is not uppercase machine format', () => {
    const result = postingRuleFormSchema.safeParse({
      ruleKey: 'sale invoice',
      description: 'x',
      isActive: true,
      lines: [debitLine(), creditLine()]
    });
    expect(result.success).toBe(false);
    expect(messages(result)).toContain('postingRules.validation.ruleKeyInvalid');
  });

  it('rejects a rule with no lines', () => {
    const result = postingRuleFormSchema.safeParse({
      ruleKey: 'EMPTY',
      description: 'x',
      isActive: true,
      lines: []
    });
    expect(result.success).toBe(false);
    expect(messages(result)).toContain('postingRules.validation.minOneLine');
  });

  it('rejects an all-debit (non-balanceable) rule', () => {
    const result = postingRuleFormSchema.safeParse({
      ruleKey: 'ALL_DEBIT',
      description: 'x',
      isActive: true,
      lines: [debitLine(), debitLine()]
    });
    expect(result.success).toBe(false);
    expect(messages(result)).toContain('postingRules.validation.notBalanceable');
  });
});

describe('applyPostingRuleFormSchema (SDD-FIN-006 §2.5)', () => {
  it('accepts non-negative amounts with a currency and entry date', () => {
    const result = applyPostingRuleFormSchema.safeParse({
      net: 100,
      tax: 20,
      gross: 120,
      currencyCode: 'BGN',
      entryDate: '2026-06-01',
      postImmediately: true
    });
    expect(result.success).toBe(true);
  });

  it('rejects a negative amount', () => {
    const result = applyPostingRuleFormSchema.safeParse({
      net: -1,
      tax: 0,
      gross: 0,
      currencyCode: 'BGN',
      entryDate: '2026-06-01',
      postImmediately: false
    });
    expect(result.success).toBe(false);
    expect(messages(result)).toContain('postingRules.validation.amountNonNegative');
  });
});

type ParseResultLike = { success: true } | { success: false; error: { issues: { message: string }[] } };

function messages(result: ParseResultLike): string[] {
  return result.success ? [] : result.error.issues.map((i) => i.message);
}
