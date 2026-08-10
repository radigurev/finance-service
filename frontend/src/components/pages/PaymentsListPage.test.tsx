import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import { AxiosError } from 'axios';
import { renderWithProviders } from '@/test/renderWithProviders';
import { PaymentsListPage, UNALLOCATED_COLUMN_WIDTH } from './PaymentsListPage';
import {
  cancelPayment,
  confirmPayment,
  getPayment,
  postPayment,
  reversePayment,
  searchPayments,
  updatePayment
} from '@/features/payments/api';
import {
  PaymentDirection,
  PaymentDocumentType,
  PaymentMethod,
  PaymentStatus,
  type PaymentDto
} from '@/features/payments/types';
import type { PagedResult } from '@/shared/api/paging';

vi.mock('@/features/payments/api');
vi.mock('@/features/accounts/api');
// The create/edit dialog mounts a nomenclature-backed currency picker; stub the hook so the page test
// makes no stray network calls.
vi.mock('@/shared/hooks/useNomenclature', () => ({
  useNomenclature: () => ({
    countries: [],
    currencies: [],
    isLoading: false,
    getStates: vi.fn(),
    getCities: vi.fn()
  })
}));

const searchPaymentsMock = vi.mocked(searchPayments);
const confirmPaymentMock = vi.mocked(confirmPayment);
const postPaymentMock = vi.mocked(postPayment);
const cancelPaymentMock = vi.mocked(cancelPayment);
const reversePaymentMock = vi.mocked(reversePayment);
const updatePaymentMock = vi.mocked(updatePayment);
const getPaymentMock = vi.mocked(getPayment);

function paged(
  items: PaymentDto[],
  over: Partial<PagedResult<PaymentDto>> = {}
): PagedResult<PaymentDto> {
  return { items, totalCount: items.length, page: 1, pageSize: 50, ...over };
}

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

function draft(over: Partial<PaymentDto> = {}): PaymentDto {
  return payment({
    status: PaymentStatus.Draft,
    documentNumber: null,
    journalEntryId: null,
    confirmedAt: null,
    postedAt: null,
    ...over
  });
}

/** Builds an Axios failure carrying a ProblemDetails `title` — the machine error code. */
function problem(status: number, title: string, detail?: string): AxiosError {
  return new AxiosError('failed', undefined, undefined, undefined, {
    status,
    data: { title, detail }
  } as never);
}

describe('PaymentsListPage (SDD-UI-FIN-002 §2.1–§2.9)', () => {
  beforeEach(() => {
    searchPaymentsMock.mockReset();
    confirmPaymentMock.mockReset();
    postPaymentMock.mockReset();
    cancelPaymentMock.mockReset();
    reversePaymentMock.mockReset();
    updatePaymentMock.mockReset();
    getPaymentMock.mockReset();
  });

  it('PaymentsList_Authenticated_RendersPagedResultEnvelope', async () => {
    searchPaymentsMock.mockResolvedValue(paged([payment()], { totalCount: 1 }));

    renderWithProviders(<PaymentsListPage />);

    expect(await screen.findByText('Payments')).toBeInTheDocument();
    expect(await screen.findByText('RCT-2026-000001')).toBeInTheDocument();
    // The query reads the { items, totalCount, page, pageSize } envelope, not a bare array.
    await waitFor(() => expect(searchPaymentsMock).toHaveBeenCalled());
  });

  it('PaymentsList_FilterSortPage_SendsFilterRequestParams_ServerSidePaging_PageSizeCapped', async () => {
    searchPaymentsMock.mockResolvedValue(paged([payment()]));

    renderWithProviders(<PaymentsListPage />);

    await waitFor(() => expect(searchPaymentsMock).toHaveBeenCalled());
    const request = searchPaymentsMock.mock.calls[0][0];
    expect(request.page).toBe(1);
    expect(request.pageSize).toBe(50);
    expect(request.pageSize).toBeLessThanOrEqual(200);
  });

  it('PaymentsList_DefaultSort_IsPaymentDateDescending', async () => {
    searchPaymentsMock.mockResolvedValue(paged([payment()]));

    renderWithProviders(<PaymentsListPage />);

    await waitFor(() => expect(searchPaymentsMock).toHaveBeenCalled());
    expect(searchPaymentsMock.mock.calls[0][0].sort).toEqual([
      { field: 'paymentDate', direction: 'desc' }
    ]);
  });

  it('PaymentsList_SearchBox_TargetsDocumentNumberOnly', async () => {
    searchPaymentsMock.mockResolvedValue(paged([payment()]));

    const { user } = renderWithProviders(<PaymentsListPage />);

    // `DocumentNumber` is the SOLE [Searchable] property, and the placeholder says so.
    const box = await screen.findByPlaceholderText('Search by document number…');
    await user.type(box, 'RCT');

    await waitFor(() => {
      const last = searchPaymentsMock.mock.calls[searchPaymentsMock.mock.calls.length - 1][0];
      expect(last.search).toBe('RCT');
    });
  });

  it('PaymentsList_CounterpartyColumn_IsNotSortable', async () => {
    searchPaymentsMock.mockResolvedValue(paged([payment()]));

    renderWithProviders(<PaymentsListPage />);

    // `CounterpartyId` is [Filterable]-only — sorting a page by an opaque GUID has no user meaning.
    const header = await screen.findByRole('columnheader', { name: 'Counterparty' });
    expect(header.className).not.toContain('columnHeader--sortable');

    // A sortable column, by contrast, carries the sortable marker; so do the other opt-in properties.
    for (const sortable of ['Number', 'Type', 'Direction', 'Method', 'Currency', 'Amount', 'Payment date', 'Status']) {
      const sortableHeader = await screen.findByRole('columnheader', { name: sortable });
      expect(sortableHeader.className).toContain('columnHeader--sortable');
    }

    // The derived allocation figures are outside the opt-in surface, so they are not sortable either.
    for (const notSortable of ['Allocated', 'Unallocated']) {
      const plainHeader = await screen.findByRole('columnheader', { name: notSortable });
      expect(plainHeader.className).not.toContain('columnHeader--sortable');
    }
  });

  it('PaymentsList_DraftRow_ShowsDashForDocumentNumber', async () => {
    searchPaymentsMock.mockResolvedValue(paged([draft()]));

    renderWithProviders(<PaymentsListPage />);

    expect(await screen.findByText('—')).toBeInTheDocument();
    expect(screen.queryByText('RCT-2026-000001')).not.toBeInTheDocument();
  });

  it('PaymentsList_CancelledRow_StillShowsDashForDocumentNumber', async () => {
    // Cancel is Draft-ONLY, so a Cancelled payment NEVER held a number and shows `—` forever. This is
    // the OPPOSITE of the invoices rule, which keeps a cancelled confirmed invoice's number.
    searchPaymentsMock.mockResolvedValue(
      paged([
        payment({
          status: PaymentStatus.Cancelled,
          documentNumber: null,
          journalEntryId: null,
          cancellationReason: 'keyed twice'
        })
      ])
    );

    renderWithProviders(<PaymentsListPage />);

    expect(await screen.findByText('Cancelled')).toBeInTheDocument();
    expect(await screen.findByText('—')).toBeInTheDocument();
    expect(screen.queryByText('RCT-2026-000001')).not.toBeInTheDocument();
  });

  it('PaymentsList_ActionsGatedByStatus_DraftEditConfirmCancelDelete_ConfirmedPostAllocate_PostedReverseAllocate', async () => {
    searchPaymentsMock.mockResolvedValue(paged([draft()]));
    const draftRender = renderWithProviders(<PaymentsListPage />);

    expect(await screen.findByLabelText('Edit')).toBeInTheDocument();
    expect(screen.getByLabelText('Confirm')).toBeInTheDocument();
    expect(screen.getByLabelText('Cancel payment')).toBeInTheDocument();
    expect(screen.getByLabelText('Delete')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Post' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reverse' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Allocate')).not.toBeInTheDocument();
    draftRender.unmount();

    searchPaymentsMock.mockResolvedValue(paged([payment({ status: PaymentStatus.Confirmed })]));
    const confirmedRender = renderWithProviders(<PaymentsListPage />);

    expect(await screen.findByRole('button', { name: 'Post' })).toBeInTheDocument();
    expect(screen.getByLabelText('Allocate')).toBeInTheDocument();
    expect(screen.getByLabelText('View allocations')).toBeInTheDocument();
    expect(screen.queryByLabelText('Cancel payment')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Edit')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Delete')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reverse' })).not.toBeInTheDocument();
    confirmedRender.unmount();

    searchPaymentsMock.mockResolvedValue(paged([payment({ status: PaymentStatus.Posted })]));
    const postedRender = renderWithProviders(<PaymentsListPage />);

    expect(await screen.findByRole('button', { name: 'Reverse' })).toBeInTheDocument();
    expect(screen.getByLabelText('Allocate')).toBeInTheDocument();
    expect(screen.getByLabelText('View allocations')).toBeInTheDocument();
    expect(screen.queryByLabelText('Cancel payment')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Edit')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Delete')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Post' })).not.toBeInTheDocument();
    postedRender.unmount();

    // Cancelled and Reversed are terminal — no mutating action at all.
    searchPaymentsMock.mockResolvedValue(
      paged([payment({ status: PaymentStatus.Reversed, reversedAt: '2026-07-02T00:00:00+00:00' })])
    );
    renderWithProviders(<PaymentsListPage />);

    expect(await screen.findByText('Reversed')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reverse' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Allocate')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Edit')).not.toBeInTheDocument();
  });

  it('PaymentsList_ConfirmedRow_DoesNotOfferCancel', async () => {
    // `Confirmed → Cancelled` was DELIBERATELY removed from AllowedNextStates. Copying the invoices
    // row action across would be a defect (§1.4 trap 3).
    searchPaymentsMock.mockResolvedValue(paged([payment({ status: PaymentStatus.Confirmed })]));

    renderWithProviders(<PaymentsListPage />);

    expect(await screen.findByRole('button', { name: 'Post' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Cancel payment')).not.toBeInTheDocument();
  });

  it('PaymentsList_DraftRow_DoesNotOfferAllocate', async () => {
    // `PaymentAllocatableValidator` requires Confirmed/Posted — a Draft can never be allocated.
    searchPaymentsMock.mockResolvedValue(paged([draft()]));

    renderWithProviders(<PaymentsListPage />);

    expect(await screen.findByLabelText('Confirm')).toBeInTheDocument();
    expect(screen.queryByLabelText('Allocate')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('View allocations')).not.toBeInTheDocument();
  });

  it('PaymentsList_PostingPending_ShowsPostingAffordance', async () => {
    searchPaymentsMock.mockResolvedValue(
      paged([payment({ status: PaymentStatus.Confirmed, journalEntryId: null })])
    );

    renderWithProviders(<PaymentsListPage />);

    expect(await screen.findByText('Posting…')).toBeInTheDocument();
  });

  it('PaymentsList_UnallocatedAmount_IsVisibleOnRow', async () => {
    searchPaymentsMock.mockResolvedValue(
      paged([payment({ amount: 100, allocatedAmount: 40, unallocatedAmount: 60 })])
    );

    renderWithProviders(<PaymentsListPage />);

    // Unapplied cash is visible without opening the payment, with a quiet "unapplied" affordance.
    expect(await screen.findByText(/60[.,]00/)).toBeInTheDocument();
    expect(await screen.findByText(/40[.,]00/)).toBeInTheDocument();
    expect(await screen.findByText('Unapplied')).toBeInTheDocument();
  });

  it('PaymentMutations_Confirm_NotDraft_ShowsPaymentNotDraftToast', async () => {
    searchPaymentsMock.mockResolvedValue(paged([draft()]));
    confirmPaymentMock.mockRejectedValue(problem(409, 'PAYMENT_NOT_DRAFT'));

    const { user } = renderWithProviders(<PaymentsListPage />);

    await user.click(await screen.findByLabelText('Confirm'));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Confirm' }));

    expect(
      await screen.findByText('Only a draft payment can be modified, deleted or cancelled.')
    ).toBeInTheDocument();
  });

  it('PaymentMutations_Confirm_YearMismatch_ShowsPaymentDateYearMismatchToast', async () => {
    searchPaymentsMock.mockResolvedValue(paged([draft()]));
    confirmPaymentMock.mockRejectedValue(problem(409, 'PAYMENT_DATE_YEAR_MISMATCH'));

    const { user } = renderWithProviders(<PaymentsListPage />);

    await user.click(await screen.findByLabelText('Confirm'));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Confirm' }));

    // The copy explains the fix (confirm inside its own year), not just the bare code.
    expect(await screen.findByText(/different year from today/i)).toBeInTheDocument();
    expect(await screen.findByText(/stays in sequence/i)).toBeInTheDocument();
  });

  it('PaymentMutations_Post_PostingPending_ShowsInformationalRetryQueued_NotDestructiveError', async () => {
    searchPaymentsMock.mockResolvedValue(
      paged([payment({ status: PaymentStatus.Confirmed, journalEntryId: null })])
    );
    postPaymentMock.mockRejectedValue(problem(409, 'PAYMENT_POSTING_PENDING'));

    const { user } = renderWithProviders(<PaymentsListPage />);

    await user.click(await screen.findByRole('button', { name: 'Post' }));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Post' }));

    // Progress, not alarm: the call re-enqueued the confirm event, so the toast says a retry is queued
    // and is enqueued as `info`, never as the oxblood `error` variant (§1.4 trap 6).
    const toast = await screen.findByText(/A retry has been queued/i);
    expect(toast).toBeInTheDocument();
    expect(toast.closest('.notistack-MuiContent-error')).toBeNull();
    expect(toast.closest('.notistack-MuiContent-info')).not.toBeNull();

    // The payment stays visible and the Post action stays available so it may be re-driven.
    // Queried INSIDE `waitFor` rather than awaited into a variable first: the failed post invalidates
    // the payments query, and the refetch's re-render can replace the grid's row nodes between a
    // `findBy*` resolving and the matcher running — which made the previous form fail on a detached
    // node under load. The assertion itself is unchanged.
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Post' })).toBeInTheDocument();
      expect(screen.getByText('Posting…')).toBeInTheDocument();
    });
  });

  it('PaymentMutations_Post_NotConfirmed_ShowsDistinctMessageFromPostingPending', async () => {
    searchPaymentsMock.mockResolvedValue(paged([payment({ status: PaymentStatus.Confirmed })]));
    postPaymentMock.mockRejectedValue(problem(409, 'PAYMENT_NOT_CONFIRMED'));

    const { user } = renderWithProviders(<PaymentsListPage />);

    await user.click(await screen.findByRole('button', { name: 'Post' }));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Post' }));

    // The two 409s exist precisely so they are distinguishable and MUST NOT collapse into one message:
    // this one is a genuine wrong-state post and reads as an error.
    const toast = await screen.findByText('The payment is not confirmed.');
    expect(toast).toBeInTheDocument();
    expect(toast.closest('.notistack-MuiContent-error')).not.toBeNull();
    expect(screen.queryByText(/A retry has been queued/i)).not.toBeInTheDocument();
  });

  it('PaymentCancel_ConfirmedPayment_MapsInvalidStateTransitionAndSuggestsReversal', async () => {
    // The UI never offers Cancel on a Confirmed payment, but the code is mapped defensively — and the
    // copy points the operator at REVERSAL as the correct correction (§2.8).
    searchPaymentsMock.mockResolvedValue(paged([draft()]));
    cancelPaymentMock.mockRejectedValue(problem(409, 'INVALID_PAYMENT_STATE_TRANSITION'));

    const { user } = renderWithProviders(<PaymentsListPage />);

    await user.click(await screen.findByLabelText('Cancel payment'));
    const dialog = await screen.findByRole('dialog');
    await user.type(within(dialog).getByRole('textbox'), 'keyed twice');
    await user.click(within(dialog).getByRole('button', { name: 'Cancel payment' }));

    expect(await screen.findByText(/reverse it/i)).toBeInTheDocument();
    expect(await screen.findByText(/transition is not allowed/i)).toBeInTheDocument();
  });

  it('PaymentReverse_AllocatedPayment_ActionDisabled_AndHasAllocationsMapped', async () => {
    searchPaymentsMock.mockResolvedValue(
      paged([payment({ status: PaymentStatus.Posted, allocatedAmount: 40, unallocatedAmount: 60 })])
    );

    const { user } = renderWithProviders(<PaymentsListPage />);

    // Pre-empted rather than letting the 409 be the first signal (§1.4 trap 12).
    const reverse = await screen.findByRole('button', { name: 'Reverse' });
    expect(reverse).toBeDisabled();
    await user.hover(reverse.parentElement as HTMLElement);
    expect(await screen.findByText(/Release them before reversing/i)).toBeInTheDocument();
    expect(reversePaymentMock).not.toHaveBeenCalled();
  });

  it('PaymentReverse_ClosedPeriod_ShowsReopenPeriodMessage_NotTryAgainLater', async () => {
    searchPaymentsMock.mockResolvedValue(paged([payment({ status: PaymentStatus.Posted })]));
    reversePaymentMock.mockRejectedValue(problem(409, 'PAYMENT_PERIOD_CLOSED'));

    const { user } = renderWithProviders(<PaymentsListPage />);

    await user.click(await screen.findByRole('button', { name: 'Reverse' }));
    const dialog = await screen.findByRole('dialog');
    await user.type(within(dialog).getByRole('textbox'), 'duplicate receipt');
    await user.click(within(dialog).getByRole('button', { name: 'Reverse' }));

    // The reversing entry keeps the ORIGINAL entry date, so the period must be REOPENED — "try again
    // later" would be wrong copy (§2.9).
    expect(await screen.findByText(/reopened/i)).toBeInTheDocument();
    expect(screen.queryByText(/try again later/i)).not.toBeInTheDocument();
  });

  it('PaymentImmutability_ConfirmedRow_NoEditOrDelete_ImmutableCodeMapped', async () => {
    searchPaymentsMock.mockResolvedValue(paged([payment({ status: PaymentStatus.Confirmed })]));
    const { unmount } = renderWithProviders(<PaymentsListPage />);

    await screen.findByRole('button', { name: 'Post' });
    expect(screen.queryByLabelText('Edit')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Delete')).not.toBeInTheDocument();
    unmount();

    // Defensive mapping for the case where the status changed underneath the operator.
    searchPaymentsMock.mockResolvedValue(paged([draft()]));
    updatePaymentMock.mockRejectedValue(problem(409, 'PAYMENT_POSTED_IMMUTABLE'));

    const { user } = renderWithProviders(<PaymentsListPage />);
    await user.click(await screen.findByLabelText('Edit'));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));

    expect(
      await screen.findByText('A confirmed or posted payment is immutable. Reverse it to correct.')
    ).toBeInTheDocument();
  });

  it('Permissions_ForbiddenListResponse_RendersEditorialForbiddenState_NoRawStatus', async () => {
    // The eight payments permissions must be seeded manually in auth-service, so every screen may
    // answer 403 on day one (§1.4 trap 13, §2.17).
    searchPaymentsMock.mockRejectedValue(problem(403, 'FORBIDDEN'));

    renderWithProviders(<PaymentsListPage />);

    expect(
      await screen.findByText('You do not have permission to view payments.')
    ).toBeInTheDocument();
    expect(await screen.findByText(/Ask an administrator/i)).toBeInTheDocument();
    // Never a raw status, never a raw key path, and no red crash toast on the route.
    expect(screen.queryByText('403')).not.toBeInTheDocument();
    expect(screen.queryByText(/^errors\./)).not.toBeInTheDocument();
    expect(document.querySelector('.notistack-MuiContent-error')).toBeNull();
    // No retry loop: the search box and the grid are replaced by the quiet panel.
    expect(screen.queryByPlaceholderText('Search by document number…')).not.toBeInTheDocument();
  });

  it('Permissions_ForbiddenOnConfirmAction_ShowsTranslatedMessage_NotTheDeveloperDetail', async () => {
    // §2.17: a 403 on an ACTION surfaces the TRANSLATED forbidden message and leaves the dialog open.
    // Without `errors.FORBIDDEN` the helper fell through to `problem.detail` and printed the backend's
    // developer English — a direct CLAUDE.md §0.3.B violation.
    const detail = "Caller lacks permission 'finance.payment:confirm'.";
    searchPaymentsMock.mockResolvedValue(paged([draft()]));
    confirmPaymentMock.mockRejectedValue(problem(403, 'FORBIDDEN', detail));

    const { user } = renderWithProviders(<PaymentsListPage />);

    await user.click(await screen.findByLabelText('Confirm'));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Confirm' }));

    const toast = await screen.findByText('You do not have permission to perform this action.');
    expect(toast).toBeInTheDocument();
    expect(screen.queryByText(detail)).not.toBeInTheDocument();
    expect(screen.queryByText(/finance\.payment:confirm/)).not.toBeInTheDocument();
    expect(screen.queryByText('403')).not.toBeInTheDocument();
    // The dialog stays open so the operator can retry once the permission is granted.
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it("Permissions_ForbiddenOnPostAction_AlsoMapsAspNetsDefault'Forbidden'Title", async () => {
    const detail = "Caller lacks permission 'finance.payment:post'.";
    searchPaymentsMock.mockResolvedValue(paged([payment({ status: PaymentStatus.Confirmed })]));
    postPaymentMock.mockRejectedValue(problem(403, 'Forbidden', detail));

    const { user } = renderWithProviders(<PaymentsListPage />);

    await user.click(await screen.findByRole('button', { name: 'Post' }));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Post' }));

    expect(
      await screen.findByText('You do not have permission to perform this action.')
    ).toBeInTheDocument();
    expect(screen.queryByText(detail)).not.toBeInTheDocument();
  });

  it('PaymentsList_Empty_OffersTheCreateActionOutsideTheGridClip', async () => {
    // The empty-state action was measured fully hidden: it sat 23px below the bottom edge of the
    // `overflow-y: hidden` virtual scroller. It must now render outside every clipping container, with
    // its description intact.
    searchPaymentsMock.mockResolvedValue(paged([], { totalCount: 0 }));

    renderWithProviders(<PaymentsListPage />);

    expect(await screen.findByText('No payments yet.')).toBeVisible();
    expect(
      await screen.findByText(/Record the first receipt or supplier payment/i)
    ).toBeVisible();

    // Two "New payment" buttons: the page header action and the empty-state action.
    const buttons = screen.getAllByRole('button', { name: 'New payment' });
    expect(buttons).toHaveLength(2);
    for (const button of buttons) {
      expect(button.closest('.MuiDataGrid-virtualScroller')).toBeNull();
      expect(button.closest('.MuiDataGrid-overlayWrapper')).toBeNull();
    }
    expect(document.querySelector('.MuiDataGrid-virtualScroller')).toBeNull();
  });

  it('PaymentsList_UnallocatedColumn_IsWideEnoughForTheFigureAndTheBadge', async () => {
    // jsdom has no text metrics, so this pins the two things that produced the browser clip: the column
    // width (160 gave `scrollWidth` 169 for a four-digit amount, truncating the badge to `UNAPPLIED…`)
    // and the one-line, non-wrapping cell layout that has to hold at compact density too.
    searchPaymentsMock.mockResolvedValue(
      paged([payment({ amount: 100000, allocatedAmount: 0, unallocatedAmount: 100000 })])
    );

    renderWithProviders(<PaymentsListPage />);

    await screen.findByText('Unapplied');
    const header = document.querySelector(
      '.MuiDataGrid-columnHeader[data-field="unallocatedAmount"]'
    ) as HTMLElement;
    const width: number = Number.parseInt(header.style.width, 10);

    expect(width).toBe(UNALLOCATED_COLUMN_WIDTH);
    // 169px was the measured overflow at EN; Cyrillic `НЕУСВОЕНО` is wider still, and the amount can
    // reach six grouped digits.
    expect(width).toBeGreaterThanOrEqual(200);

    // Figure and badge live in ONE cell, on ONE line.
    const cell = document.querySelector(
      '.MuiDataGrid-cell[data-field="unallocatedAmount"]'
    ) as HTMLElement;
    // The grouping separator is whatever the active locale uses (space, comma or NBSP).
    expect(cell.textContent).toMatch(/100.?000[.,]00/);
    expect(cell.textContent).toContain('Unapplied');
    const badge = within(cell).getByText('Unapplied');
    expect(window.getComputedStyle(badge).whiteSpace).toBe('nowrap');
  });

  it('PaymentError_UnmappedCode_FallsBackToGenericError', async () => {
    searchPaymentsMock.mockRejectedValue(problem(500, 'TOTALLY_UNKNOWN_CODE'));

    renderWithProviders(<PaymentsListPage />);

    // The helper falls back to the ProblemDetails title when no `errors.<CODE>` exists — never a raw
    // i18n key path.
    expect(await screen.findByText('TOTALLY_UNKNOWN_CODE')).toBeInTheDocument();
    expect(screen.queryByText(/^errors\./)).not.toBeInTheDocument();
  });
});
