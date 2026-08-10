import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import { AxiosError } from 'axios';
import { renderWithProviders } from '@/test/renderWithProviders';
import { PaymentFormDialog } from './PaymentFormDialog';
import { CancelPaymentDialog } from './CancelPaymentDialog';
import { ReversePaymentDialog } from './ReversePaymentDialog';
import { cancelPayment, createPayment, reversePayment, updatePayment } from '@/features/payments/api';
import { searchAccounts } from '@/features/accounts/api';
import { AccountType } from '@/features/accounts/types';
import {
  PaymentDirection,
  PaymentDocumentType,
  PaymentMethod,
  PaymentStatus,
  type PaymentDto
} from '@/features/payments/types';

vi.mock('@/features/payments/api');
vi.mock('@/features/accounts/api');
vi.mock('@/shared/hooks/useNomenclature', () => ({
  useNomenclature: () => ({
    countries: [],
    currencies: [
      { code: 'BGN', name: 'Bulgarian Lev' },
      { code: 'EUR', name: 'Euro' }
    ],
    isLoading: false,
    getStates: vi.fn(),
    getCities: vi.fn()
  })
}));

const createPaymentMock = vi.mocked(createPayment);
const updatePaymentMock = vi.mocked(updatePayment);
const cancelPaymentMock = vi.mocked(cancelPayment);
const reversePaymentMock = vi.mocked(reversePayment);
const searchAccountsMock = vi.mocked(searchAccounts);

const COUNTERPARTY = '22222222-2222-2222-2222-222222222222';

function payment(over: Partial<PaymentDto> = {}): PaymentDto {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    documentNumber: null,
    documentType: PaymentDocumentType.SupplierPayment,
    direction: PaymentDirection.AP,
    method: PaymentMethod.BankTransfer,
    status: PaymentStatus.Draft,
    counterpartyId: COUNTERPARTY,
    currencyCode: 'EUR',
    baseCurrencyCode: 'BGN',
    amount: 100,
    exchangeRate: 1.955831,
    baseAmount: 195.58,
    settlementAccountId: 7,
    paymentDate: '2026-07-01T00:00:00+00:00',
    bankReference: null,
    allocatedAmount: 0,
    unallocatedAmount: 100,
    journalEntryId: null,
    cancellationReason: null,
    createdAt: '2026-07-01T00:00:00+00:00',
    confirmedAt: null,
    postedAt: null,
    reversedAt: null,
    rowVersion: 'AAAA',
    ...over
  };
}

/** Builds an Axios failure carrying a ProblemDetails `title` — the machine error code. */
function problem(status: number, title: string): AxiosError {
  return new AxiosError('failed', undefined, undefined, undefined, {
    status,
    data: { title }
  } as never);
}

/** Fills the create form's required fields so a submit reaches the API. */
async function fillCreateForm(user: ReturnType<typeof renderWithProviders>['user']) {
  const dialog = await screen.findByRole('dialog');
  await user.type(within(dialog).getByPlaceholderText('Counterparty id (GUID)'), COUNTERPARTY);

  const comboboxes = within(dialog).getAllByRole('combobox');
  // Order: document type, method, currency, settlement account.
  await user.click(comboboxes[2]);
  await user.click(await screen.findByRole('option', { name: 'BGN' }));
  await user.click(comboboxes[3]);
  await user.click(await screen.findByRole('option', { name: /1010/ }));

  const numbers = within(dialog).getAllByRole('spinbutton');
  await user.clear(numbers[0]);
  await user.type(numbers[0], '250.50');
  return dialog;
}

describe('PaymentFormDialog (SDD-UI-FIN-002 §2.3, §2.5)', () => {
  beforeEach(() => {
    createPaymentMock.mockReset();
    updatePaymentMock.mockReset();
    searchAccountsMock.mockReset();
    searchAccountsMock.mockResolvedValue({
      items: [
        {
          id: 5,
          code: '1010',
          name: 'Cash on hand',
          type: AccountType.Asset,
          parentId: null,
          isActive: true,
          countryCode: 'BG',
          rowVersion: 'ACC1'
        }
      ],
      totalCount: 1,
      page: 1,
      pageSize: 200
    });
  });

  it('PaymentForm_DirectionDerivedFromDocumentType_ShownReadOnly', async () => {
    const { user } = renderWithProviders(
      <PaymentFormDialog open payment={null} onClose={vi.fn()} onSaved={vi.fn()} />
    );

    const dialog = await screen.findByRole('dialog');
    // The default type is a customer receipt, so the derived direction reads AR.
    const directionField = within(dialog).getByDisplayValue('AR');
    expect(directionField).toHaveAttribute('readonly');

    // Switching to a supplier payment flips the read-only feedback to AP…
    const comboboxes = within(dialog).getAllByRole('combobox');
    await user.click(comboboxes[0]);
    await user.click(await screen.findByRole('option', { name: 'Supplier payment' }));
    expect(within(dialog).getByDisplayValue('AP')).toHaveAttribute('readonly');

    // …and `direction` is NEVER part of the request body — the server derives it.
    await fillCreateForm(user);
    createPaymentMock.mockResolvedValue(payment());
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(createPaymentMock).toHaveBeenCalled());
    const request = createPaymentMock.mock.calls[0][0] as unknown as Record<string, unknown>;
    expect(request).not.toHaveProperty('direction');
    expect(request).not.toHaveProperty('baseCurrencyCode');
    expect(request).not.toHaveProperty('baseAmount');
  });

  it('PaymentForm_DocumentTypeReadOnlyInEditMode_AndSentUnchanged', async () => {
    const draft = payment({ documentType: PaymentDocumentType.SupplierPayment });
    updatePaymentMock.mockResolvedValue(draft);

    const { user } = renderWithProviders(
      <PaymentFormDialog open payment={draft} onClose={vi.fn()} onSaved={vi.fn()} />
    );

    const dialog = await screen.findByRole('dialog');
    // The type drives the direction, the sequence key, and the posting rule, so it cannot be changed.
    const comboboxes = within(dialog).getAllByRole('combobox');
    expect(comboboxes[0]).toHaveAttribute('aria-disabled', 'true');

    await user.click(within(dialog).getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(updatePaymentMock).toHaveBeenCalled());
    // `UpdatePaymentRequest` carries the type precisely so the server can reject a change; it is sent
    // back UNCHANGED from the persisted payment.
    expect(updatePaymentMock.mock.calls[0][1].documentType).toBe(
      PaymentDocumentType.SupplierPayment
    );
    expect(updatePaymentMock.mock.calls[0][1].rowVersion).toBe('AAAA');
  });

  it('PaymentForm_BaseAmountPreview_RecomputesOnAmountAndRateChange_ServerOverridesAfterSave', async () => {
    const { user, unmount } = renderWithProviders(
      <PaymentFormDialog open payment={null} onClose={vi.fn()} onSaved={vi.fn()} />
    );

    const dialog = await screen.findByRole('dialog');
    expect(
      within(dialog).getByText(/Preview only — the server computes the authoritative base amount/i)
    ).toBeInTheDocument();

    // amount 100 × rate 1.955831 previews 195.58 — the preview must RECOMPUTE from the watched
    // fields (the regression the invoices feature hit), not stay at 0.00.
    const numbers = within(dialog).getAllByRole('spinbutton');
    await user.clear(numbers[0]);
    await user.type(numbers[0], '100');
    await user.clear(numbers[1]);
    await user.type(numbers[1], '1.955831');

    await waitFor(() => expect(within(dialog).getByText(/195[.,]58/)).toBeInTheDocument());
    unmount();

    // In edit mode the PERSISTED server figure is displayed, not the client arithmetic.
    renderWithProviders(
      <PaymentFormDialog
        open
        payment={payment({ amount: 100, exchangeRate: 1.955831, baseAmount: 195.59 })}
        onClose={vi.fn()}
        onSaved={vi.fn()}
      />
    );
    const editDialog = await screen.findByRole('dialog');
    expect(within(editDialog).getByText(/195[.,]59/)).toBeInTheDocument();
    expect(within(editDialog).queryByText(/195[.,]58/)).not.toBeInTheDocument();
  });

  it('PaymentForm_RateEqualsOneRule_IsSoftHintOnly_NotBlocking', async () => {
    // The base currency is only readable from a RESPONSE, so the rule cannot be enforced up front.
    // In edit mode the base currency IS known: a base-currency payment with a rate other than one
    // gets a quiet hint, and the submit still goes through — the server is the authority.
    const draft = payment({ currencyCode: 'BGN', baseCurrencyCode: 'BGN', exchangeRate: 1.2 });
    updatePaymentMock.mockResolvedValue(draft);

    const { user } = renderWithProviders(
      <PaymentFormDialog open payment={draft} onClose={vi.fn()} onSaved={vi.fn()} />
    );

    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/rate is expected to be exactly 1\.000000/i)).toBeInTheDocument();

    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(updatePaymentMock).toHaveBeenCalled());
    expect(updatePaymentMock.mock.calls[0][1].exchangeRate).toBe(1.2);
  });

  it('PaymentMutations_Create_InvalidatesListAndShowsSuccess', async () => {
    createPaymentMock.mockResolvedValue(payment());
    const onSaved = vi.fn();

    const { user } = renderWithProviders(
      <PaymentFormDialog open payment={null} onClose={vi.fn()} onSaved={onSaved} />
    );

    const dialog = await fillCreateForm(user);
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(createPaymentMock).toHaveBeenCalled());
    const request = createPaymentMock.mock.calls[0][0];
    expect(request.documentType).toBe(PaymentDocumentType.CustomerReceipt);
    expect(request.counterpartyId).toBe(COUNTERPARTY);
    expect(request.currencyCode).toBe('BGN');
    expect(request.amount).toBe(250.5);
    expect(request.settlementAccountId).toBe(5);
    expect(await screen.findByText('Payment created.')).toBeInTheDocument();
    expect(onSaved).toHaveBeenCalled();
  });

  it('PaymentMutations_Edit_StaleRowVersion_ShowsConcurrentModificationToast', async () => {
    updatePaymentMock.mockRejectedValue(problem(409, 'CONCURRENT_MODIFICATION'));
    const onSaved = vi.fn();

    const { user } = renderWithProviders(
      <PaymentFormDialog open payment={payment()} onClose={vi.fn()} onSaved={onSaved} />
    );

    await user.click(await screen.findByRole('button', { name: 'Save' }));

    expect(await screen.findByText(/This record was changed by someone else/i)).toBeInTheDocument();
    // The dialog stays open on failure so nothing typed is lost.
    expect(onSaved).not.toHaveBeenCalled();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('blocks an invalid submit client-side before any request is issued', async () => {
    const { user } = renderWithProviders(
      <PaymentFormDialog open payment={null} onClose={vi.fn()} onSaved={vi.fn()} />
    );

    await user.click(await screen.findByRole('button', { name: 'Save' }));

    expect(
      await screen.findByText('A valid counterparty identifier is required.')
    ).toBeInTheDocument();
    expect(createPaymentMock).not.toHaveBeenCalled();
  });

  it('PaymentForm_EverySelectIsLabelledWithoutAnInvalidLabelFor (ui-validate D7 — a11y)', async () => {
    // `Type`, `Method`, `Currency` and `Cash / bank account` all render as `<div role="combobox">`.
    // A `<label for>` pointing at one of those is invalid HTML and Chrome flags it; the label must be
    // referenced BY the control via `aria-labelledby` instead.
    renderWithProviders(
      <PaymentFormDialog open payment={null} onClose={vi.fn()} onSaved={vi.fn()} />
    );

    const dialog = await screen.findByRole('dialog');
    const labelable: string[] = ['INPUT', 'SELECT', 'TEXTAREA', 'BUTTON'];

    for (const label of Array.from(dialog.querySelectorAll('label[for]'))) {
      const target = document.getElementById(label.getAttribute('for') as string);
      expect(labelable, `label "${label.textContent}" → <${target?.tagName}>`).toContain(
        target?.tagName
      );
    }

    for (const name of ['Type', 'Method', 'Currency', 'Cash / bank account']) {
      const control = within(dialog).getByLabelText(new RegExp(`^${name}`));
      expect(control).toHaveAttribute('role', 'combobox');
      expect(control.getAttribute('aria-labelledby')).toBeTruthy();
    }
  });
});

describe('CancelPaymentDialog (SDD-UI-FIN-002 §2.8)', () => {
  beforeEach(() => {
    cancelPaymentMock.mockReset();
  });

  it('PaymentCancel_EmptyReason_SubmitDisabled_AndServerCodeMapped', async () => {
    cancelPaymentMock.mockRejectedValue(problem(400, 'PAYMENT_CANCEL_REASON_REQUIRED'));

    const { user } = renderWithProviders(
      <CancelPaymentDialog payment={payment()} onClose={vi.fn()} onCancelled={vi.fn()} />
    );

    const dialog = await screen.findByRole('dialog');
    // Submitting with an empty reason is blocked by the inline reason-required validation — no request.
    await user.click(within(dialog).getByRole('button', { name: 'Cancel payment' }));
    expect(await within(dialog).findByText('A reason is required.')).toBeInTheDocument();
    expect(cancelPaymentMock).not.toHaveBeenCalled();

    // With a reason the call goes out, and the server code is still mapped defensively.
    await user.type(within(dialog).getByRole('textbox'), 'keyed twice');
    await user.click(within(dialog).getByRole('button', { name: 'Cancel payment' }));

    await waitFor(() => expect(cancelPaymentMock).toHaveBeenCalled());
    expect(cancelPaymentMock.mock.calls[0][1]).toEqual({
      reason: 'keyed twice',
      rowVersion: 'AAAA'
    });
    expect(
      await screen.findByText('A reason is required to cancel a payment.')
    ).toBeInTheDocument();
  });

  it('names the draft without fabricating a document number it never held', async () => {
    renderWithProviders(
      <CancelPaymentDialog payment={payment()} onClose={vi.fn()} onCancelled={vi.fn()} />
    );

    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/never held a document number/i)).toBeInTheDocument();
  });
});

describe('ReversePaymentDialog (SDD-UI-FIN-002 §2.9)', () => {
  beforeEach(() => {
    reversePaymentMock.mockReset();
  });

  it('PaymentReverse_EmptyReason_SubmitDisabled_AndServerCodeMapped', async () => {
    reversePaymentMock.mockRejectedValue(problem(400, 'PAYMENT_REVERSE_REASON_REQUIRED'));
    const posted = payment({
      status: PaymentStatus.Posted,
      documentNumber: 'PAY-2026-000004',
      journalEntryId: 'je-1'
    });

    const { user } = renderWithProviders(
      <ReversePaymentDialog payment={posted} onClose={vi.fn()} onReversed={vi.fn()} />
    );

    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Reverse' }));
    expect(await within(dialog).findByText('A reason is required.')).toBeInTheDocument();
    expect(reversePaymentMock).not.toHaveBeenCalled();

    await user.type(within(dialog).getByRole('textbox'), 'duplicate receipt');
    await user.click(within(dialog).getByRole('button', { name: 'Reverse' }));

    await waitFor(() => expect(reversePaymentMock).toHaveBeenCalled());
    expect(reversePaymentMock.mock.calls[0][1]).toEqual({
      reason: 'duplicate receipt',
      rowVersion: 'AAAA'
    });
    expect(
      await screen.findByText('A reason is required to reverse a payment.')
    ).toBeInTheDocument();
  });

  it('explains that reversal is a sign-flipped entry, not an edit or a deletion', async () => {
    const posted = payment({
      status: PaymentStatus.Posted,
      documentNumber: 'PAY-2026-000004',
      journalEntryId: 'je-1'
    });

    renderWithProviders(
      <ReversePaymentDialog payment={posted} onClose={vi.fn()} onReversed={vi.fn()} />
    );

    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/sign-flipped journal entry/i)).toBeInTheDocument();
    expect(within(dialog).getByText(/keeps its number/i)).toBeInTheDocument();
    expect(within(dialog).getByText(/PAY-2026-000004/)).toBeInTheDocument();
  });
});
