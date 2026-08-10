import { describe, it, expect } from 'vitest';
import {
  allocateFormSchema,
  decimalPlaces,
  paymentFormSchema,
  parseBuckets,
  previewBaseAmount,
  validateAgingQuery,
  validateBalancesQuery,
  validateBuckets,
  validateOpenItemQuery
} from './schema';
import { PaymentDocumentType, PaymentMethod } from './types';

const COUNTERPARTY = '22222222-2222-2222-2222-222222222222';
const INVOICE_A = '44444444-4444-4444-4444-444444444444';
const INVOICE_B = '55555555-5555-5555-5555-555555555555';

/** A valid payment form value set; individual tests override the field under test. */
function form(over: Record<string, unknown> = {}) {
  return {
    documentType: PaymentDocumentType.CustomerReceipt,
    method: PaymentMethod.BankTransfer,
    counterpartyId: COUNTERPARTY,
    currencyCode: 'BGN',
    amount: 100,
    exchangeRate: 1,
    settlementAccountId: 7,
    paymentDate: '2026-07-01',
    bankReference: '',
    ...over
  };
}

/** Collects every issue message so a test can assert one specific rule fired. */
function messages(result: { success: boolean; error?: { issues: { message: string }[] } }): string[] {
  return result.success ? [] : (result.error?.issues ?? []).map((issue) => issue.message);
}

describe('paymentFormSchema (SDD-UI-FIN-002 §3.1)', () => {
  it('accepts a well-formed draft', () => {
    expect(paymentFormSchema.safeParse(form()).success).toBe(true);
  });

  it('PaymentForm_MissingCounterparty_ShowsCounterpartyRequired', () => {
    expect(messages(paymentFormSchema.safeParse(form({ counterpartyId: '' })))).toContain(
      'payments.validation.counterpartyRequired'
    );
    // Guid.Empty is rejected by the backend, so it is rejected here too.
    expect(
      messages(
        paymentFormSchema.safeParse(form({ counterpartyId: '00000000-0000-0000-0000-000000000000' }))
      )
    ).toContain('payments.validation.counterpartyRequired');
  });

  it('PaymentForm_NonPositiveAmount_ShowsAmountPositive', () => {
    expect(messages(paymentFormSchema.safeParse(form({ amount: 0 })))).toContain(
      'payments.validation.amountPositive'
    );
    expect(messages(paymentFormSchema.safeParse(form({ amount: -5 })))).toContain(
      'payments.validation.amountPositive'
    );
  });

  it('PaymentForm_AmountWithThreeDecimals_ShowsAmountScale', () => {
    expect(messages(paymentFormSchema.safeParse(form({ amount: 10.005 })))).toContain(
      'payments.validation.amountScale'
    );
    // Two decimals are fine — the money scale is DECIMAL(18,2).
    expect(paymentFormSchema.safeParse(form({ amount: 10.05 })).success).toBe(true);
  });

  it('rejects a rate with more than six decimal places and a non-positive rate', () => {
    expect(messages(paymentFormSchema.safeParse(form({ exchangeRate: 1.1234567 })))).toContain(
      'payments.validation.exchangeRateScale'
    );
    expect(messages(paymentFormSchema.safeParse(form({ exchangeRate: 0 })))).toContain(
      'payments.validation.exchangeRatePositive'
    );
    expect(paymentFormSchema.safeParse(form({ exchangeRate: 1.955831 })).success).toBe(true);
  });

  it('PaymentForm_FuturePaymentDate_ShowsPaymentDateFuture', () => {
    const tomorrow: string = new Date(Date.now() + 86_400_000).toISOString().slice(0, 10);
    expect(messages(paymentFormSchema.safeParse(form({ paymentDate: tomorrow })))).toContain(
      'payments.validation.paymentDateFuture'
    );
    expect(messages(paymentFormSchema.safeParse(form({ paymentDate: '' })))).toContain(
      'payments.validation.paymentDateRequired'
    );
  });

  it('PaymentForm_BankReferenceOver64Chars_ShowsBankReferenceTooLong', () => {
    expect(messages(paymentFormSchema.safeParse(form({ bankReference: 'x'.repeat(65) })))).toContain(
      'payments.validation.bankReferenceTooLong'
    );
    expect(paymentFormSchema.safeParse(form({ bankReference: 'x'.repeat(64) })).success).toBe(true);
  });

  it('requires a positive settlement account and a 3-letter currency', () => {
    expect(messages(paymentFormSchema.safeParse(form({ settlementAccountId: 0 })))).toContain(
      'payments.validation.settlementAccountRequired'
    );
    expect(messages(paymentFormSchema.safeParse(form({ currencyCode: 'bg' })))).toContain(
      'payments.validation.currencyRequired'
    );
  });

  it('previews the base amount without ever blocking on its own arithmetic', () => {
    expect(previewBaseAmount(100, 1.955831)).toBe(195.58);
    expect(previewBaseAmount(0, 0)).toBe(0);
  });
});

describe('allocateFormSchema (SDD-UI-FIN-002 §3.2, §3.4)', () => {
  const context = {
    unallocatedAmount: 100,
    outstandingByInvoice: { [INVOICE_A]: 60, [INVOICE_B]: 60 }
  };

  it('accepts a selection inside every bound', () => {
    const result = allocateFormSchema(context).safeParse({
      items: [
        { invoiceId: INVOICE_A, allocatedAmount: 40 },
        { invoiceId: INVOICE_B, allocatedAmount: 60 }
      ]
    });
    expect(result.success).toBe(true);
  });

  it('AllocateForm_EmptyItems_ShowsItemsRequired', () => {
    // An empty list is NEVER read as "apply the whole payment" (§1.6 gap 5).
    expect(messages(allocateFormSchema(context).safeParse({ items: [] }))).toContain(
      'allocations.validation.itemsRequired'
    );
  });

  it('AllocateForm_AmountWithThreeDecimals_ShowsAmountScale', () => {
    expect(
      messages(
        allocateFormSchema(context).safeParse({
          items: [{ invoiceId: INVOICE_A, allocatedAmount: 10.001 }]
        })
      )
    ).toContain('allocations.validation.amountScale');
  });

  it('AllocateForm_SumOverUnallocated_ShowsExceedsUnallocated', () => {
    expect(
      messages(
        allocateFormSchema(context).safeParse({
          items: [
            { invoiceId: INVOICE_A, allocatedAmount: 60 },
            { invoiceId: INVOICE_B, allocatedAmount: 60 }
          ]
        })
      )
    ).toContain('allocations.validation.exceedsUnallocated');
  });

  it('AllocateForm_ItemOverOutstanding_ShowsExceedsOutstanding', () => {
    expect(
      messages(
        allocateFormSchema(context).safeParse({
          items: [{ invoiceId: INVOICE_A, allocatedAmount: 61 }]
        })
      )
    ).toContain('allocations.validation.exceedsOutstanding');
  });

  it('AllocateForm_DuplicateInvoiceWithinRequest_ShowsDuplicateInvoice', () => {
    expect(
      messages(
        allocateFormSchema(context).safeParse({
          items: [
            { invoiceId: INVOICE_A, allocatedAmount: 20 },
            { invoiceId: INVOICE_A, allocatedAmount: 20 }
          ]
        })
      )
    ).toContain('allocations.validation.duplicateInvoice');
  });

  it('AllocateForm_OneCentOverBound_Fails_NoToleranceBand', () => {
    // The server compares exact DECIMAL(18,2) values with NO tolerance, so the client uses the same
    // exactness — one cent over either bound fails, and exactly on the bound passes (§2.18).
    const onePastPayment = allocateFormSchema(context).safeParse({
      items: [{ invoiceId: INVOICE_A, allocatedAmount: 60 }, { invoiceId: INVOICE_B, allocatedAmount: 40.01 }]
    });
    expect(messages(onePastPayment)).toContain('allocations.validation.exceedsUnallocated');

    const onePastInvoice = allocateFormSchema(context).safeParse({
      items: [{ invoiceId: INVOICE_A, allocatedAmount: 60.01 }]
    });
    expect(messages(onePastInvoice)).toContain('allocations.validation.exceedsOutstanding');

    const exactlyOnBound = allocateFormSchema(context).safeParse({
      items: [{ invoiceId: INVOICE_A, allocatedAmount: 60 }, { invoiceId: INVOICE_B, allocatedAmount: 40 }]
    });
    expect(exactlyOnBound.success).toBe(true);
  });

  it('counts decimal places exactly, with no floating-point slack', () => {
    expect(decimalPlaces(10)).toBe(0);
    expect(decimalPlaces(10.5)).toBe(1);
    expect(decimalPlaces(10.05)).toBe(2);
    expect(decimalPlaces(10.005)).toBe(3);
    expect(decimalPlaces(1.955831)).toBe(6);
  });
});

describe('Aging + balances query validation (SDD-UI-FIN-002 §3.3)', () => {
  const base = {
    asOfDate: '2026-07-01',
    direction: 'AR',
    counterpartyId: '',
    currencyCode: '',
    buckets: [] as number[]
  };

  it('Aging_MoreThanSixBoundaries_ShowsBucketsTooMany', () => {
    expect(validateBuckets([10, 20, 30, 40, 50, 60, 70])).toBe('aging.validation.bucketsTooMany');
    expect(validateAgingQuery({ ...base, buckets: [10, 20, 30, 40, 50, 60, 70] }).buckets).toBe(
      'aging.validation.bucketsTooMany'
    );
    // Exactly six is allowed.
    expect(validateBuckets([10, 20, 30, 40, 50, 60])).toBeUndefined();
  });

  it('Aging_NonAscendingBoundaries_ShowsBucketsAscending', () => {
    expect(validateBuckets([30, 30, 90])).toBe('aging.validation.bucketsAscending');
    expect(validateBuckets([90, 60, 30])).toBe('aging.validation.bucketsAscending');
    expect(validateAgingQuery({ ...base, buckets: [30, 20] }).buckets).toBe(
      'aging.validation.bucketsAscending'
    );
  });

  it('Aging_NonPositiveBoundary_ShowsBucketsPositive', () => {
    expect(validateBuckets([0, 30, 60])).toBe('aging.validation.bucketsPositive');
    expect(validateBuckets([-30])).toBe('aging.validation.bucketsPositive');
    expect(validateBuckets([30.5])).toBe('aging.validation.bucketsPositive');
    // A non-numeric fragment parses to NaN and is rejected by the same rule.
    expect(validateBuckets(parseBuckets('30, abc'))).toBe('aging.validation.bucketsPositive');
  });

  it('treats a blank bucket entry as "not customized" so the server default applies', () => {
    expect(parseBuckets('')).toEqual([]);
    expect(parseBuckets('  ')).toEqual([]);
    expect(parseBuckets('30, 60, 90')).toEqual([30, 60, 90]);
    expect(validateBuckets([])).toBeUndefined();
  });

  it('Balances_RequiresAsOfDateAndDirection_ShowsValidationWhenMissing', () => {
    const errors = validateBalancesQuery({ asOfDate: '', direction: '', currencyCode: '' });
    expect(errors.asOfDate).toBe('aging.validation.asOfDateRequired');
    expect(errors.direction).toBe('aging.validation.directionRequired');

    const future: string = new Date(Date.now() + 86_400_000).toISOString().slice(0, 10);
    expect(validateBalancesQuery({ asOfDate: future, direction: 'AR', currencyCode: '' }).asOfDate).toBe(
      'aging.validation.asOfDateFuture'
    );
    expect(
      validateBalancesQuery({ asOfDate: '2026-07-01', direction: 'AR', currencyCode: 'bg' })
        .currencyCode
    ).toBe('aging.validation.currencyInvalid');
    expect(
      Object.keys(validateBalancesQuery({ asOfDate: '2026-07-01', direction: 'AP', currencyCode: 'EUR' }))
    ).toEqual([]);
  });

  it('leaves every open-items narrowing optional but still blocks a future as-of date', () => {
    expect(
      Object.keys(validateOpenItemQuery({ asOfDate: '', counterpartyId: '', currencyCode: '' }))
    ).toEqual([]);

    const future: string = new Date(Date.now() + 86_400_000).toISOString().slice(0, 10);
    expect(
      validateOpenItemQuery({ asOfDate: future, counterpartyId: '', currencyCode: '' }).asOfDate
    ).toBe('aging.validation.asOfDateFuture');
    expect(
      validateOpenItemQuery({ asOfDate: '', counterpartyId: 'not-a-guid', currencyCode: '' })
        .counterpartyId
    ).toBe('aging.validation.counterpartyInvalid');
  });
});
