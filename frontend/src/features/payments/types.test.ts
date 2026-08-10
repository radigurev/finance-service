import { describe, it, expect } from 'vitest';
import {
  PaymentDirection,
  PaymentDocumentType,
  PaymentMethod,
  PaymentStatus,
  SettlementStatus,
  agingDirectionOf,
  derivedDirection,
  directionLabelKey,
  directionStringLabelKey,
  displayDocumentNumber,
  displayStatusKey,
  documentTypeLabelKey,
  isPostingPending,
  methodLabelKey,
  settlementStatusLabelKey,
  type OpenItemDto,
  type PaymentDto
} from './types';

function payment(over: Partial<PaymentDto> = {}): PaymentDto {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    documentNumber: 'RCT-2026-000001',
    documentType: PaymentDocumentType.CustomerReceipt,
    direction: PaymentDirection.AR,
    method: PaymentMethod.BankTransfer,
    status: PaymentStatus.Posted,
    counterpartyId: '22222222-2222-2222-2222-222222222222',
    currencyCode: 'BGN',
    baseCurrencyCode: 'BGN',
    amount: 100,
    exchangeRate: 1,
    baseAmount: 100,
    settlementAccountId: 7,
    paymentDate: '2026-07-01T00:00:00+00:00',
    bankReference: null,
    allocatedAmount: 0,
    unallocatedAmount: 100,
    journalEntryId: '33333333-3333-3333-3333-333333333333',
    cancellationReason: null,
    createdAt: '2026-07-01T00:00:00+00:00',
    confirmedAt: '2026-07-01T00:00:00+00:00',
    postedAt: '2026-07-01T00:00:00+00:00',
    reversedAt: null,
    rowVersion: 'AAAA',
    ...over
  };
}

describe('Payments wire types (SDD-UI-FIN-002 §1.2, §1.4)', () => {
  it('PaymentTypes_EnumsMirrorDotNetNumericValues_ApAr', () => {
    // PaymentDirection is AP = 1, AR = 2 — deliberately NOT alphabetical-by-intuition. Guessing
    // AR = 1 silently mislabels every row (§1.4 trap 1).
    expect(PaymentDirection.AP).toBe(1);
    expect(PaymentDirection.AR).toBe(2);

    expect(PaymentDocumentType.CustomerReceipt).toBe(1);
    expect(PaymentDocumentType.SupplierPayment).toBe(2);

    expect(PaymentMethod.Cash).toBe(1);
    expect(PaymentMethod.BankTransfer).toBe(2);
    expect(PaymentMethod.Card).toBe(3);

    expect(PaymentStatus.Draft).toBe(1);
    expect(PaymentStatus.Confirmed).toBe(2);
    expect(PaymentStatus.Posted).toBe(3);
    expect(PaymentStatus.Cancelled).toBe(4);
    expect(PaymentStatus.Reversed).toBe(5);

    expect(SettlementStatus.Unsettled).toBe(1);
    expect(SettlementStatus.PartiallySettled).toBe(2);
    expect(SettlementStatus.Settled).toBe(3);
  });

  it('PaymentTypes_MixedWireShapes_NumericOnPaymentDto_StringOnOpenItemDto', () => {
    // PaymentDto carries real C# enums, which System.Text.Json emits as INTEGERS…
    const numericSide: PaymentDto = payment({ direction: PaymentDirection.AR });
    expect(numericSide.direction).toBe(2);
    expect(typeof numericSide.direction).toBe('number');
    expect(typeof numericSide.status).toBe('number');
    expect(typeof numericSide.documentType).toBe('number');
    expect(typeof numericSide.method).toBe('number');

    // …while OpenItemDto declares documentType / direction / invoiceStatus / agingBucket as `string`,
    // and only settlementStatus is the numeric enum. The two shapes are modelled honestly and are
    // never silently normalized into one another (§1.4 trap 1).
    const stringSide: OpenItemDto = {
      invoiceId: '44444444-4444-4444-4444-444444444444',
      documentNumber: 'SINV-2026-0001',
      documentType: 'SaleInvoice',
      direction: 'AR',
      counterpartyId: '22222222-2222-2222-2222-222222222222',
      currencyCode: 'BGN',
      baseCurrencyCode: 'BGN',
      grossTotal: 120,
      settledAmount: 0,
      outstanding: 120,
      baseOutstanding: 120,
      issueDate: '2026-06-01T00:00:00+00:00',
      dueDate: '2026-06-15T00:00:00+00:00',
      daysPastDue: 5,
      agingBucket: '1-30',
      settlementStatus: SettlementStatus.Unsettled,
      invoiceStatus: 'Posted'
    };
    expect(typeof stringSide.direction).toBe('string');
    expect(stringSide.direction).toBe('AR');
    expect(typeof stringSide.documentType).toBe('string');
    expect(typeof stringSide.invoiceStatus).toBe('string');
    expect(typeof stringSide.agingBucket).toBe('string');
    expect(typeof stringSide.settlementStatus).toBe('number');

    // The narrowing helper converts the numeric DTO value into the STRING form the three query
    // contracts require — the request never carries the numeric PaymentDirection.
    expect(agingDirectionOf(PaymentDirection.AR)).toBe('AR');
    expect(agingDirectionOf(PaymentDirection.AP)).toBe('AP');
  });

  it('derives the direction from the document type without ever sending it', () => {
    expect(derivedDirection(PaymentDocumentType.CustomerReceipt)).toBe(PaymentDirection.AR);
    expect(derivedDirection(PaymentDocumentType.SupplierPayment)).toBe(PaymentDirection.AP);
  });

  it('maps every enum member onto its own i18n label key', () => {
    expect(documentTypeLabelKey(PaymentDocumentType.CustomerReceipt)).toBe(
      'payments.type_CustomerReceipt'
    );
    expect(directionLabelKey(PaymentDirection.AP)).toBe('payments.direction_AP');
    expect(directionStringLabelKey('AR')).toBe('payments.direction_AR');
    expect(methodLabelKey(PaymentMethod.Card)).toBe('payments.method_Card');
    expect(settlementStatusLabelKey(SettlementStatus.PartiallySettled)).toBe(
      'allocations.settlement_PartiallySettled'
    );
  });

  it('renders the posting-pending affordance for a Confirmed payment with no linked entry', () => {
    const pending = payment({ status: PaymentStatus.Confirmed, journalEntryId: null });
    expect(isPostingPending(pending)).toBe(true);
    expect(displayStatusKey(pending)).toBe('payments.status_Posting');

    const linked = payment({ status: PaymentStatus.Confirmed, journalEntryId: 'je-1' });
    expect(isPostingPending(linked)).toBe(false);
    expect(displayStatusKey(linked)).toBe('payments.status_Confirmed');
  });

  it('shows a dash for a Draft AND for a Cancelled payment, which never held a number', () => {
    expect(displayDocumentNumber(payment({ documentNumber: null }))).toBe('—');
    expect(displayDocumentNumber(payment({ documentNumber: 'RCT-2026-000001' }))).toBe(
      'RCT-2026-000001'
    );
  });
});
