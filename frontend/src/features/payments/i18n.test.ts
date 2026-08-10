import { describe, it, expect } from 'vitest';
import { en } from '@/shared/i18n/locales/en';
import { bg } from '@/shared/i18n/locales/bg';

type Dict = Record<string, unknown>;

/** Recursively collects dotted key paths from a nested locale object. */
function flattenKeys(obj: Dict, prefix = ''): string[] {
  return Object.entries(obj).flatMap(([key, value]) => {
    const path = prefix ? `${prefix}.${key}` : key;
    if (value !== null && typeof value === 'object' && !Array.isArray(value)) {
      return flattenKeys(value as Dict, path);
    }
    return [path];
  });
}

const enKeys: string[] = flattenKeys(en as unknown as Dict);
const bgKeys: string[] = flattenKeys(bg as unknown as Dict);

/** The five new key GROUPS this feature introduces (SDD-UI-FIN-002 §5). */
const KEY_GROUPS: string[] = ['payments.', 'allocations.', 'openItems.', 'aging.', 'balances.'];

/** Keys named explicitly in §5 that MUST resolve, grouped by the section that names them. */
const REQUIRED_KEYS: string[] = [
  'nav.payments',
  'nav.openItems',
  'nav.aging',
  // payments — titles / nav / empty
  'payments.title',
  'payments.newPayment',
  'payments.detailTitle',
  'payments.createTitle',
  'payments.editTitle',
  'payments.searchPlaceholder',
  'payments.empty',
  'payments.emptyHint',
  'payments.forbidden',
  'payments.forbiddenHint',
  // payments — columns / fields
  'payments.documentNumber',
  'payments.documentType',
  'payments.direction',
  'payments.method',
  'payments.status',
  'payments.counterparty',
  'payments.counterpartyPlaceholder',
  'payments.currency',
  'payments.amount',
  'payments.exchangeRate',
  'payments.baseCurrency',
  'payments.baseAmount',
  'payments.baseAmountPreview',
  'payments.settlementAccount',
  'payments.paymentDate',
  'payments.bankReference',
  'payments.allocatedAmount',
  'payments.unallocatedAmount',
  'payments.unapplied',
  'payments.journalEntry',
  'payments.cancellationReason',
  'payments.createdAt',
  'payments.confirmedAt',
  'payments.postedAt',
  'payments.reversedAt',
  // payments — document types / directions / methods / statuses
  'payments.type_CustomerReceipt',
  'payments.type_SupplierPayment',
  'payments.direction_AP',
  'payments.direction_AR',
  'payments.method_Cash',
  'payments.method_BankTransfer',
  'payments.method_Card',
  'payments.status_Draft',
  'payments.status_Confirmed',
  'payments.status_Posting',
  'payments.status_Posted',
  'payments.status_Cancelled',
  'payments.status_Reversed',
  // payments — actions
  'payments.edit',
  'payments.delete',
  'payments.confirm',
  'payments.post',
  'payments.cancel',
  'payments.reverse',
  'payments.allocate',
  'payments.viewAllocations',
  // payments — dialogs / hints / toasts
  'payments.confirmTitle',
  'payments.confirmMessage',
  'payments.postTitle',
  'payments.postMessage',
  'payments.postingPendingHint',
  'payments.postingPendingQueued',
  'payments.cancelTitle',
  'payments.cancelMessage',
  'payments.cancelReasonLabel',
  'payments.cancelNotAvailableHint',
  'payments.reverseTitle',
  'payments.reverseMessage',
  'payments.reverseReasonLabel',
  'payments.reverseBlockedByAllocations',
  'payments.deleteTitle',
  'payments.deleteMessage',
  'payments.created',
  'payments.updated',
  'payments.deleted',
  'payments.confirmed',
  'payments.posted',
  'payments.cancelled',
  'payments.reversed',
  // payments — validation
  'payments.validation.documentTypeRequired',
  'payments.validation.methodRequired',
  'payments.validation.counterpartyRequired',
  'payments.validation.currencyRequired',
  'payments.validation.amountPositive',
  'payments.validation.amountScale',
  'payments.validation.exchangeRatePositive',
  'payments.validation.exchangeRateScale',
  'payments.validation.paymentDateRequired',
  'payments.validation.paymentDateFuture',
  'payments.validation.settlementAccountRequired',
  'payments.validation.bankReferenceTooLong',
  'payments.validation.cancelReasonRequired',
  'payments.validation.reverseReasonRequired',
  // allocations — panel / columns
  'allocations.title',
  'allocations.allocate',
  'allocations.deallocate',
  'allocations.empty',
  'allocations.emptyHint',
  'allocations.invoice',
  'allocations.invoiceDueDate',
  'allocations.invoiceStatus',
  'allocations.invoiceGrossTotal',
  'allocations.allocatedAmount',
  'allocations.baseAllocatedAmount',
  'allocations.realizedFxDifference',
  'allocations.realizedFxInformational',
  'allocations.allocatedAt',
  'allocations.runningTotal',
  'allocations.remainingUnallocated',
  'allocations.pickerTitle',
  'allocations.pickerHint',
  'allocations.applyMax',
  // allocations — settlement statuses (the numeric enum's three members)
  'allocations.settlement_Unsettled',
  'allocations.settlement_PartiallySettled',
  'allocations.settlement_Settled',
  // allocations — dialogs / toasts
  'allocations.allocateTitle',
  'allocations.allocateMessage',
  'allocations.deallocateTitle',
  'allocations.deallocateMessage',
  'allocations.deallocateReasonLabel',
  'allocations.noAmendHint',
  'allocations.allocated',
  'allocations.deallocated',
  // allocations — validation
  'allocations.validation.itemsRequired',
  'allocations.validation.invoiceRequired',
  'allocations.validation.amountPositive',
  'allocations.validation.amountScale',
  'allocations.validation.exceedsUnallocated',
  'allocations.validation.exceedsOutstanding',
  'allocations.validation.duplicateInvoice',
  // openItems
  'openItems.title',
  'openItems.empty',
  'openItems.emptyHint',
  'openItems.forbidden',
  'openItems.asOfDate',
  'openItems.direction',
  'openItems.counterparty',
  'openItems.currency',
  'openItems.overdueOnly',
  'openItems.documentNumber',
  'openItems.documentType',
  'openItems.grossTotal',
  'openItems.settledAmount',
  'openItems.outstanding',
  'openItems.baseOutstanding',
  'openItems.bookingRateHint',
  'openItems.issueDate',
  'openItems.dueDate',
  'openItems.daysPastDue',
  'openItems.notYetDue',
  'openItems.agingBucket',
  'openItems.invoiceStatus',
  'openItems.settlementStatus',
  'openItems.eventualConsistencyHint',
  'openItems.creditNoteExcludedHint',
  // aging
  'aging.title',
  'aging.asOfDate',
  'aging.direction',
  'aging.counterparty',
  'aging.currency',
  'aging.buckets',
  'aging.bucketsHint',
  'aging.bucketsDefaultHint',
  'aging.bucket_Current',
  'aging.openItemCount',
  'aging.totalOutstanding',
  'aging.totalBaseOutstanding',
  'aging.reportTotals',
  'aging.grandTotalBaseOutstanding',
  'aging.baseCurrencyOnlyHint',
  'aging.periodAgnosticHint',
  'aging.invoiceOnlyHint',
  'aging.drillDown',
  'aging.empty',
  'aging.emptyHint',
  'aging.forbidden',
  'aging.forbiddenHint',
  // aging — validation
  'aging.validation.asOfDateRequired',
  'aging.validation.asOfDateFuture',
  'aging.validation.directionRequired',
  'aging.validation.bucketsAscending',
  'aging.validation.bucketsPositive',
  'aging.validation.bucketsTooMany',
  'aging.validation.currencyInvalid',
  'aging.validation.counterpartyInvalid',
  // balances
  'balances.title',
  'balances.counterparty',
  'balances.currency',
  'balances.openItemCount',
  'balances.outstanding',
  'balances.baseOutstanding',
  'balances.overdueOutstanding',
  'balances.baseOverdueOutstanding',
  'balances.oldestDueDate',
  'balances.noOpenItems',
  'balances.overdueDefinitionHint',
  'balances.matchesAgingHint',
  'balances.noSortingHint',
  'balances.empty',
  'balances.emptyHint',
  'balances.forbidden'
];

describe('Payments_I18n_AllKeysExistInEnAndBg (SDD-UI-FIN-002 §5)', () => {
  it('defines every key group named by the spec in BOTH locales', () => {
    const missingInEn: string[] = REQUIRED_KEYS.filter((key) => !enKeys.includes(key));
    const missingInBg: string[] = REQUIRED_KEYS.filter((key) => !bgKeys.includes(key));

    expect({ missingInEn, missingInBg }).toEqual({ missingInEn: [], missingInBg: [] });
    expect(REQUIRED_KEYS.length).toBeGreaterThan(190);
  });

  it.each(KEY_GROUPS)('keeps every %s* key in strict EN/BG parity', (group) => {
    const enGroup: string[] = enKeys.filter((k) => k.startsWith(group)).sort();
    const bgGroup: string[] = bgKeys.filter((k) => k.startsWith(group)).sort();

    expect(enGroup.length).toBeGreaterThan(0);
    expect(bgGroup).toEqual(enGroup);
  });

  it('never resolves a payments-surface key to its own raw key path', () => {
    for (const group of KEY_GROUPS) {
      for (const key of enKeys.filter((k) => k.startsWith(group))) {
        const enValue = readValue(en as unknown as Dict, key);
        const bgValue = readValue(bg as unknown as Dict, key);
        expect(typeof enValue).toBe('string');
        expect(typeof bgValue).toBe('string');
        expect(String(enValue).trim().length).toBeGreaterThan(0);
        expect(String(bgValue).trim().length).toBeGreaterThan(0);
        expect(enValue).not.toBe(key);
        expect(bgValue).not.toBe(key);
      }
    }
  });

  it('gives the BG payments strings Cyrillic text rather than untranslated English', () => {
    // The direction and bucket labels are deliberately latin/numeric codes in both locales, so they
    // are exempt from the Cyrillic assertion.
    const latinByDesign: string[] = ['payments.direction_AP', 'payments.direction_AR'];

    for (const group of KEY_GROUPS) {
      for (const key of bgKeys.filter((k) => k.startsWith(group))) {
        if (latinByDesign.includes(key)) {
          continue;
        }
        expect(String(readValue(bg as unknown as Dict, key))).toMatch(/[Ѐ-ӿ]/);
      }
    }
  });
});

describe('Payments_I18n_ReusesShippedPaymentErrorCodeEntries_WithoutDuplication (SDD-UI-FIN-002 §4)', () => {
  /** A representative slice of the 43 codes already pinned by `paymentErrorCodes.test.ts`. */
  const SHIPPED_CODES: string[] = [
    'PAYMENT_NOT_FOUND',
    'PAYMENT_NOT_DRAFT',
    'PAYMENT_NOT_CONFIRMED',
    'PAYMENT_POSTING_PENDING',
    'PAYMENT_POSTED_IMMUTABLE',
    'PAYMENT_DATE_YEAR_MISMATCH',
    'PAYMENT_HAS_ALLOCATIONS',
    'PAYMENT_ALLOCATION_DUPLICATE',
    'INVALID_AGING_BUCKETS',
    'INVALID_AGING_AS_OF_DATE'
  ];

  it('reuses the shipped errors.<CODE> entries rather than re-declaring them under a new group', () => {
    for (const code of SHIPPED_CODES) {
      expect(enKeys).toContain(`errors.${code}`);
      expect(bgKeys).toContain(`errors.${code}`);
      // The 43 codes live ONLY under `errors.*`; a second copy under any new group would mean the
      // feature duplicated an already-discharged obligation.
      expect(enKeys.filter((k) => k.endsWith(`.${code}`))).toEqual([`errors.${code}`]);
      expect(bgKeys.filter((k) => k.endsWith(`.${code}`))).toEqual([`errors.${code}`]);
    }
  });

  it('keeps the shared CONCURRENT_MODIFICATION / PAGE_SIZE_TOO_LARGE / GENERIC_ERROR fallbacks', () => {
    for (const code of ['CONCURRENT_MODIFICATION', 'PAGE_SIZE_TOO_LARGE', 'GENERIC_ERROR']) {
      expect(enKeys).toContain(`errors.${code}`);
      expect(bgKeys).toContain(`errors.${code}`);
    }
  });
});

/** Reads a dotted key path back out of a nested locale object. */
function readValue(obj: Dict, path: string): unknown {
  return path.split('.').reduce<unknown>((acc, segment) => {
    if (acc !== null && typeof acc === 'object') {
      return (acc as Dict)[segment];
    }
    return undefined;
  }, obj);
}
