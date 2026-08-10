import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import { AxiosError } from 'axios';
import { renderWithProviders } from '@/test/renderWithProviders';
import { PaymentAllocationsDialog } from './PaymentAllocationsDialog';
import { AllocatePaymentDialog } from './AllocatePaymentDialog';
import {
  allocatePayment,
  deallocatePayment,
  getPayment,
  searchOpenItems,
  searchPaymentAllocations
} from '@/features/payments/api';
import {
  PaymentDirection,
  PaymentDocumentType,
  PaymentMethod,
  PaymentStatus,
  SettlementStatus,
  type AllocatePaymentResultDto,
  type DeallocatePaymentResultDto,
  type OpenItemDto,
  type PaymentAllocationDto,
  type PaymentDto
} from '@/features/payments/types';
import type { PagedResult } from '@/shared/api/paging';

vi.mock('@/features/payments/api');

const searchPaymentAllocationsMock = vi.mocked(searchPaymentAllocations);
const searchOpenItemsMock = vi.mocked(searchOpenItems);
const allocatePaymentMock = vi.mocked(allocatePayment);
const deallocatePaymentMock = vi.mocked(deallocatePayment);
const getPaymentMock = vi.mocked(getPayment);

const COUNTERPARTY = '22222222-2222-2222-2222-222222222222';
const INVOICE_A = '44444444-4444-4444-4444-444444444444';
const INVOICE_B = '55555555-5555-5555-5555-555555555555';

function paged<T>(items: T[]): PagedResult<T> {
  return { items, totalCount: items.length, page: 1, pageSize: 25 };
}

function payment(over: Partial<PaymentDto> = {}): PaymentDto {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    documentNumber: 'RCT-2026-000001',
    documentType: PaymentDocumentType.CustomerReceipt,
    direction: PaymentDirection.AR,
    method: PaymentMethod.BankTransfer,
    status: PaymentStatus.Posted,
    counterpartyId: COUNTERPARTY,
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

function allocation(over: Partial<PaymentAllocationDto> = {}): PaymentAllocationDto {
  return {
    id: 7,
    paymentId: '11111111-1111-1111-1111-111111111111',
    invoiceId: INVOICE_A,
    allocatedAmount: 40,
    baseAllocatedAmount: 40,
    realizedFxDifference: 0,
    allocatedAt: '2026-07-02T00:00:00+00:00',
    invoiceDocumentNumber: 'SINV-2026-0001',
    invoiceDueDate: '2026-06-15T00:00:00+00:00',
    invoiceStatus: 'Posted',
    invoiceGrossTotal: 120,
    invoiceSettlementStatus: SettlementStatus.PartiallySettled,
    ...over
  };
}

function openItem(over: Partial<OpenItemDto> = {}): OpenItemDto {
  return {
    invoiceId: INVOICE_A,
    documentNumber: 'SINV-2026-0001',
    documentType: 'SaleInvoice',
    direction: 'AR',
    counterpartyId: COUNTERPARTY,
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
    invoiceStatus: 'Posted',
    ...over
  };
}

function allocateResult(over: Partial<AllocatePaymentResultDto> = {}): AllocatePaymentResultDto {
  return {
    paymentId: '11111111-1111-1111-1111-111111111111',
    allocations: [allocation()],
    allocatedAmount: 40,
    unallocatedAmount: 60,
    rowVersion: 'BBBB',
    affectedInvoices: [
      {
        invoiceId: INVOICE_A,
        settledAmount: 40,
        settlementStatus: SettlementStatus.PartiallySettled
      }
    ],
    ...over
  };
}

function deallocateResult(): DeallocatePaymentResultDto {
  return {
    paymentId: '11111111-1111-1111-1111-111111111111',
    allocationId: 7,
    invoiceId: INVOICE_A,
    releasedAmount: 40,
    allocatedAmount: 0,
    unallocatedAmount: 100,
    rowVersion: 'CCCC',
    affectedInvoice: {
      invoiceId: INVOICE_A,
      settledAmount: 0,
      settlementStatus: SettlementStatus.Unsettled
    }
  };
}

/** Builds an Axios failure carrying a ProblemDetails `title` — the machine error code. */
function problem(status: number, title: string): AxiosError {
  return new AxiosError('failed', undefined, undefined, undefined, {
    status,
    data: { title }
  } as never);
}

describe('PaymentAllocationsDialog (SDD-UI-FIN-002 §2.10, §2.12)', () => {
  beforeEach(() => {
    searchPaymentAllocationsMock.mockReset();
    deallocatePaymentMock.mockReset();
    getPaymentMock.mockReset();
  });

  it('Allocations_EmptyList_RendersEmptyStateNotError', async () => {
    // An unallocated payment is a NORMAL business state, never an error.
    searchPaymentAllocationsMock.mockResolvedValue(paged<PaymentAllocationDto>([]));

    renderWithProviders(
      <PaymentAllocationsDialog
        payment={payment()}
        onClose={vi.fn()}
        onAllocate={vi.fn()}
        onDeallocated={vi.fn()}
      />
    );

    expect(await screen.findByText('No allocations on this payment.')).toBeInTheDocument();
    expect(await screen.findByText(/Unapplied cash is a normal state/i)).toBeInTheDocument();
    // A quiet allocate action is offered from the empty state, and no error toast is raised.
    expect((await screen.findAllByRole('button', { name: 'Allocate' })).length).toBeGreaterThan(0);
    expect(document.querySelector('.notistack-MuiContent-error')).toBeNull();
  });

  it('Allocations_ListSortSurface_AllocatedAtAndAmountOnly_InvoiceIdNotSortable', async () => {
    searchPaymentAllocationsMock.mockResolvedValue(paged([allocation()]));

    renderWithProviders(
      <PaymentAllocationsDialog
        payment={payment()}
        onClose={vi.fn()}
        onAllocate={vi.fn()}
        onDeallocated={vi.fn()}
      />
    );

    // `AllocatedAmount` and `AllocatedAt` are the only filterable+sortable properties.
    for (const sortable of ['Allocated', 'Allocated on']) {
      const header = await screen.findByRole('columnheader', { name: sortable });
      expect(header.className).toContain('columnHeader--sortable');
    }
    // `InvoiceId` is [Filterable]-only, so the invoice column exposes no sort.
    const invoiceHeader = await screen.findByRole('columnheader', { name: 'Invoice' });
    expect(invoiceHeader.className).not.toContain('columnHeader--sortable');

    // No [Searchable] property exists on PaymentAllocation, so no search box is offered.
    expect(screen.queryByPlaceholderText(/Search/i)).not.toBeInTheDocument();

    // The default order is AllocatedAt descending.
    await waitFor(() => expect(searchPaymentAllocationsMock).toHaveBeenCalled());
    expect(searchPaymentAllocationsMock.mock.calls[0][1].sort).toEqual([
      { field: 'allocatedAt', direction: 'desc' }
    ]);
  });

  it('Allocations_SettlementStatus_RenderedFromNumericEnum_NotRederivedClientSide', async () => {
    // settledAmount 120 of a 120 gross total would look "Settled" if re-derived client-side, but the
    // server's numeric SettlementStatus says PartiallySettled — the server owns the single
    // SettlementStatusCalculator and its answer is what renders (§2.10).
    searchPaymentAllocationsMock.mockResolvedValue(
      paged([
        allocation({
          allocatedAmount: 120,
          invoiceGrossTotal: 120,
          invoiceSettlementStatus: SettlementStatus.PartiallySettled
        })
      ])
    );

    renderWithProviders(
      <PaymentAllocationsDialog
        payment={payment()}
        onClose={vi.fn()}
        onAllocate={vi.fn()}
        onDeallocated={vi.fn()}
      />
    );

    expect(await screen.findByText('Partially settled')).toBeInTheDocument();
    expect(screen.queryByText('Settled')).not.toBeInTheDocument();
    // The mirrored invoice status is a STRING on the wire and renders verbatim.
    expect(await screen.findByText('Posted')).toBeInTheDocument();
  });

  it('Allocations_RealizedFxDifference_LabelledInformational', async () => {
    searchPaymentAllocationsMock.mockResolvedValue(paged([allocation({ realizedFxDifference: 0 })]));

    renderWithProviders(
      <PaymentAllocationsDialog
        payment={payment()}
        onClose={vi.fn()}
        onAllocate={vi.fn()}
        onDeallocated={vi.fn()}
      />
    );

    // Never presented as a posted GL amount, and a zero value renders 0.00 rather than blank.
    expect(
      await screen.findByText(/realized FX difference is informational only/i)
    ).toBeInTheDocument();
    expect(await screen.findByText(/is not posted to the ledger yet/i)).toBeInTheDocument();
    const zeros = await screen.findAllByText(/^0[.,]00$/);
    expect(zeros.length).toBeGreaterThan(0);
  });

  it('renders a Cancelled or Reversed payment’s allocation rows as read-only history', async () => {
    searchPaymentAllocationsMock.mockResolvedValue(paged([allocation()]));

    renderWithProviders(
      <PaymentAllocationsDialog
        payment={payment({ status: PaymentStatus.Reversed, allocatedAmount: 40 })}
        onClose={vi.fn()}
        onAllocate={vi.fn()}
        onDeallocated={vi.fn()}
      />
    );

    expect(await screen.findByText('SINV-2026-0001')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Release' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Allocate' })).not.toBeInTheDocument();
  });

  it('Deallocate_Success_ConsumesResultDto_ReseedsRowVersion', async () => {
    searchPaymentAllocationsMock.mockResolvedValue(paged([allocation()]));
    deallocatePaymentMock.mockResolvedValue(deallocateResult());
    const onDeallocated = vi.fn();

    const { user } = renderWithProviders(
      <PaymentAllocationsDialog
        payment={payment({ allocatedAmount: 40, unallocatedAmount: 60 })}
        rowVersion="AAAA"
        onClose={vi.fn()}
        onAllocate={vi.fn()}
        onDeallocated={onDeallocated}
      />
    );

    await user.click(await screen.findByRole('button', { name: 'Release' }));
    const confirm = await screen.findByText(/Release this match\?/i);
    const confirmDialog = confirm.closest('[role="dialog"]') as HTMLElement;
    // The copy states that nothing is posted or reversed and that there is no in-place amendment.
    expect(within(confirmDialog).getByText(/Nothing is posted, nothing is reversed/i)).toBeInTheDocument();
    await user.type(within(confirmDialog).getByRole('textbox'), 'wrong amount');
    await user.click(within(confirmDialog).getByRole('button', { name: 'Release' }));

    await waitFor(() => expect(deallocatePaymentMock).toHaveBeenCalled());
    expect(deallocatePaymentMock.mock.calls[0]).toEqual([
      '11111111-1111-1111-1111-111111111111',
      7,
      { rowVersion: 'AAAA', reason: 'wrong amount' }
    ]);

    // The result DTO is consumed and the fresh rowVersion is handed back for chaining, with NO
    // follow-up read (§1.4 trap 11).
    await waitFor(() =>
      expect(onDeallocated).toHaveBeenCalledWith(
        expect.objectContaining({ rowVersion: 'CCCC', releasedAmount: 40, unallocatedAmount: 100 })
      )
    );
    expect(getPaymentMock).not.toHaveBeenCalled();
    expect(await screen.findByText('Allocation released.')).toBeInTheDocument();
  });

  it('Deallocate_ForeignAllocationId_MapsAllocationNotFound', async () => {
    // The lookup is scoped by (paymentId, allocationId), so a row belonging to another payment reads
    // as not found — and the message must NOT imply someone else deleted it.
    searchPaymentAllocationsMock.mockResolvedValue(paged([allocation()]));
    deallocatePaymentMock.mockRejectedValue(problem(404, 'PAYMENT_ALLOCATION_NOT_FOUND'));

    const { user } = renderWithProviders(
      <PaymentAllocationsDialog
        payment={payment({ allocatedAmount: 40, unallocatedAmount: 60 })}
        onClose={vi.fn()}
        onAllocate={vi.fn()}
        onDeallocated={vi.fn()}
      />
    );

    await user.click(await screen.findByRole('button', { name: 'Release' }));
    const confirm = await screen.findByText(/Release this match\?/i);
    const confirmDialog = confirm.closest('[role="dialog"]') as HTMLElement;
    await user.click(within(confirmDialog).getByRole('button', { name: 'Release' }));

    expect(await screen.findByText('Allocation not found.')).toBeInTheDocument();
    expect(screen.queryByText(/deleted by/i)).not.toBeInTheDocument();
  });
});

describe('AllocatePaymentDialog (SDD-UI-FIN-002 §2.11)', () => {
  beforeEach(() => {
    searchOpenItemsMock.mockReset();
    searchPaymentAllocationsMock.mockReset();
    allocatePaymentMock.mockReset();
    getPaymentMock.mockReset();
    searchPaymentAllocationsMock.mockResolvedValue(paged<PaymentAllocationDto>([]));
  });

  it('AllocatePicker_PreNarrowsOpenItemsByCounterpartyCurrencyDirection', async () => {
    searchOpenItemsMock.mockResolvedValue(paged([openItem()]));

    renderWithProviders(
      <AllocatePaymentDialog payment={payment()} onClose={vi.fn()} onAllocated={vi.fn()} />
    );

    await waitFor(() => expect(searchOpenItemsMock).toHaveBeenCalled());
    const [narrowing, request] = searchOpenItemsMock.mock.calls[0];
    expect(narrowing.counterpartyId).toBe(COUNTERPARTY);
    expect(narrowing.currencyCode).toBe('BGN');
    // The numeric PaymentDto.direction (AR = 2) is narrowed into the STRING form the query requires.
    expect(narrowing.direction).toBe('AR');
    expect(request.sort).toEqual([{ field: 'dueDate', direction: 'asc' }]);
    expect(request.pageSize).toBeLessThanOrEqual(200);
  });

  it('sends "AP" for a supplier payment, whose numeric direction is 1', async () => {
    searchOpenItemsMock.mockResolvedValue(paged<OpenItemDto>([]));

    renderWithProviders(
      <AllocatePaymentDialog
        payment={payment({
          documentType: PaymentDocumentType.SupplierPayment,
          direction: PaymentDirection.AP
        })}
        onClose={vi.fn()}
        onAllocated={vi.fn()}
      />
    );

    await waitFor(() => expect(searchOpenItemsMock).toHaveBeenCalled());
    expect(searchOpenItemsMock.mock.calls[0][0].direction).toBe('AP');
  });

  it('AllocatePicker_ExcludesInvoicesAlreadyAllocatedToThisPayment', async () => {
    // `/open-items` has no "exclude allocated by payment X" narrowing and a partly matched invoice
    // keeps a positive outstanding, so the payment's own allocations are cross-referenced to keep
    // PAYMENT_ALLOCATION_DUPLICATE off the routine path (§1.6 gap 7).
    searchOpenItemsMock.mockResolvedValue(
      paged([
        openItem({ invoiceId: INVOICE_A, documentNumber: 'SINV-2026-0001', outstanding: 80 }),
        openItem({ invoiceId: INVOICE_B, documentNumber: 'SINV-2026-0002', outstanding: 50 })
      ])
    );
    searchPaymentAllocationsMock.mockResolvedValue(paged([allocation({ invoiceId: INVOICE_A })]));

    renderWithProviders(
      <AllocatePaymentDialog payment={payment()} onClose={vi.fn()} onAllocated={vi.fn()} />
    );

    expect(await screen.findByText('SINV-2026-0002')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByText('SINV-2026-0001')).not.toBeInTheDocument());
  });

  it('Allocate_Success_ConsumesResultDto_ReseedsRowVersion_UpdatesSettlementState', async () => {
    searchOpenItemsMock.mockResolvedValue(paged([openItem({ outstanding: 40 })]));
    allocatePaymentMock.mockResolvedValue(allocateResult());
    const onAllocated = vi.fn();

    const { user } = renderWithProviders(
      <AllocatePaymentDialog
        payment={payment({ unallocatedAmount: 100 })}
        rowVersion="AAAA"
        onClose={vi.fn()}
        onAllocated={onAllocated}
      />
    );

    // The amount defaults to min(outstanding, unallocated) = 40.
    const checkbox = await screen.findByRole('checkbox', { name: /SINV-2026-0001/ });
    await user.click(checkbox);
    expect(await screen.findByDisplayValue('40')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Allocate' }));

    await waitFor(() => expect(allocatePaymentMock).toHaveBeenCalled());
    expect(allocatePaymentMock.mock.calls[0][1]).toEqual({
      items: [{ invoiceId: INVOICE_A, allocatedAmount: 40 }],
      rowVersion: 'AAAA'
    });

    // 200 (not 201) with no Location: the result already carries the new figures, the affected
    // invoices' settlement state, and the new rowVersion — so NO follow-up read is issued.
    await waitFor(() =>
      expect(onAllocated).toHaveBeenCalledWith(
        expect.objectContaining({
          rowVersion: 'BBBB',
          allocatedAmount: 40,
          unallocatedAmount: 60,
          affectedInvoices: [
            {
              invoiceId: INVOICE_A,
              settledAmount: 40,
              settlementStatus: SettlementStatus.PartiallySettled
            }
          ]
        })
      )
    );
    expect(getPaymentMock).not.toHaveBeenCalled();
  });

  it('Allocate_Failure_IsAllOrNothing_NoPartialOptimisticUpdate', async () => {
    searchOpenItemsMock.mockResolvedValue(
      paged([
        openItem({ invoiceId: INVOICE_A, documentNumber: 'SINV-2026-0001', outstanding: 40 }),
        openItem({ invoiceId: INVOICE_B, documentNumber: 'SINV-2026-0002', outstanding: 30 })
      ])
    );
    allocatePaymentMock.mockRejectedValue(problem(409, 'PAYMENT_ALLOCATION_EXCEEDS_OUTSTANDING'));
    const onAllocated = vi.fn();

    const { user } = renderWithProviders(
      <AllocatePaymentDialog
        payment={payment({ unallocatedAmount: 100 })}
        onClose={vi.fn()}
        onAllocated={onAllocated}
      />
    );

    await user.click(await screen.findByRole('checkbox', { name: /SINV-2026-0001/ }));
    await user.click(await screen.findByRole('checkbox', { name: /SINV-2026-0002/ }));
    await user.click(screen.getByRole('button', { name: 'Allocate' }));

    expect(
      await screen.findByText('The allocation exceeds the outstanding amount of the invoice.')
    ).toBeInTheDocument();
    // Nothing is optimistically decremented, no partial success is reported, and the dialog stays open.
    expect(onAllocated).not.toHaveBeenCalled();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(await screen.findByRole('checkbox', { name: /SINV-2026-0001/ })).toBeChecked();
  });

  it('blocks an over-allocation client-side before any request, with exact two-decimal bounds', async () => {
    searchOpenItemsMock.mockResolvedValue(paged([openItem({ outstanding: 120 })]));

    const { user } = renderWithProviders(
      <AllocatePaymentDialog
        payment={payment({ unallocatedAmount: 50 })}
        onClose={vi.fn()}
        onAllocated={vi.fn()}
      />
    );

    await user.click(await screen.findByRole('checkbox', { name: /SINV-2026-0001/ }));
    const amount = await screen.findByDisplayValue('50');
    await user.clear(amount);
    await user.type(amount, '50.01');

    // One cent over the payment bound fails, with no tolerance band.
    expect(
      await screen.findByText('The selected total exceeds the unallocated amount of the payment.')
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Allocate' })).toBeDisabled();
    expect(allocatePaymentMock).not.toHaveBeenCalled();
  });

  it('Allocate_TenInvariantCodes_EachMapsToItsOwnMessage', async () => {
    // All ten allocation 404/409 codes must resolve to DISTINCT, non-generic messages that tell the
    // operator what to change — never a shared "allocation failed" (§2.11, §4).
    const codes: string[] = [
      'PAYMENT_ALLOCATION_INVOICE_NOT_FOUND',
      'PAYMENT_ALLOCATION_INVOICE_NOT_ELIGIBLE',
      'PAYMENT_ALLOCATION_DIRECTION_MISMATCH',
      'PAYMENT_ALLOCATION_COUNTERPARTY_MISMATCH',
      'PAYMENT_ALLOCATION_CURRENCY_MISMATCH',
      'PAYMENT_ALLOCATION_DUPLICATE',
      'PAYMENT_ALLOCATION_EXCEEDS_PAYMENT',
      'PAYMENT_ALLOCATION_EXCEEDS_OUTSTANDING',
      'PAYMENT_ALLOCATION_CONTROL_ACCOUNT_MISMATCH',
      'PAYMENT_NOT_ALLOCATABLE'
    ];
    const seen = new Set<string>();

    for (const code of codes) {
      searchOpenItemsMock.mockResolvedValue(paged([openItem({ outstanding: 40 })]));
      allocatePaymentMock.mockReset();
      allocatePaymentMock.mockRejectedValue(problem(409, code));

      const { user, unmount } = renderWithProviders(
        <AllocatePaymentDialog
          payment={payment({ unallocatedAmount: 100 })}
          onClose={vi.fn()}
          onAllocated={vi.fn()}
        />
      );

      await user.click(await screen.findByRole('checkbox', { name: /SINV-2026-0001/ }));
      await user.click(screen.getByRole('button', { name: 'Allocate' }));

      const toast = await waitFor(() => {
        const node = document.querySelector('.notistack-MuiContent-error');
        expect(node).not.toBeNull();
        return node as HTMLElement;
      });
      const message: string = toast.textContent ?? '';

      expect(message.trim().length).toBeGreaterThan(0);
      // Never the raw code, never a raw key path, never the generic fallback.
      expect(message).not.toContain(code);
      expect(message).not.toContain('errors.');
      expect(message).not.toContain('Something went wrong');
      // Every code gets its OWN message.
      expect(seen.has(message)).toBe(false);
      seen.add(message);

      unmount();
    }

    expect(seen.size).toBe(codes.length);
  });
});
