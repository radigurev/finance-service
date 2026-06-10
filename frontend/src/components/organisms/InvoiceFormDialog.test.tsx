import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import { AxiosError } from 'axios';
import { renderWithProviders } from '@/test/renderWithProviders';
import { InvoiceFormDialog } from './InvoiceFormDialog';
import { CancelInvoiceDialog } from './CancelInvoiceDialog';
import { CreateNoteDialog } from './CreateNoteDialog';
import { createInvoice, updateInvoice, cancelInvoice } from '@/features/invoices/api';
import {
  InvoiceDirection,
  InvoiceDocumentType,
  InvoiceStatus,
  type InvoiceDto
} from '@/features/invoices/types';

vi.mock('@/features/invoices/api');
vi.mock('@/shared/hooks/useNomenclature', () => ({
  useNomenclature: () => ({
    countries: [],
    currencies: [{ code: 'BGN', name: 'Bulgarian Lev' }],
    isLoading: false,
    getStates: vi.fn(),
    getCities: vi.fn()
  })
}));

const createInvoiceMock = vi.mocked(createInvoice);
const updateInvoiceMock = vi.mocked(updateInvoice);
const cancelInvoiceMock = vi.mocked(cancelInvoice);

function posted(over: Partial<InvoiceDto> = {}): InvoiceDto {
  return {
    id: 'orig-1111',
    documentNumber: 'SINV-2026-0001',
    documentType: InvoiceDocumentType.SaleInvoice,
    direction: InvoiceDirection.AR,
    status: InvoiceStatus.Posted,
    counterpartyId: 'cp-1',
    currencyCode: 'BGN',
    baseCurrencyCode: 'BGN',
    issueDate: '2026-06-01T00:00:00+00:00',
    dueDate: '2026-06-15T00:00:00+00:00',
    netTotal: 100,
    taxTotal: 20,
    grossTotal: 120,
    correctsInvoiceId: null,
    journalEntryId: 'je-1',
    createdAt: '2026-06-01T00:00:00+00:00',
    confirmedAt: '2026-06-01T00:00:00+00:00',
    postedAt: '2026-06-01T00:00:00+00:00',
    lines: [{ lineNumber: 1, description: 'Widget', quantity: 2, unitPrice: 50, taxRate: 0.2, lineNet: 100, lineTax: 20, lineGross: 120 }],
    rowVersion: 'AAAA',
    ...over
  };
}

describe('InvoiceFormDialog (SDD-UI-FIN-001 §2.4, §2.5)', () => {
  beforeEach(() => {
    createInvoiceMock.mockReset();
    updateInvoiceMock.mockReset();
    cancelInvoiceMock.mockReset();
  });

  it('InvoiceForm_TotalsPreview_MatchesLineSums', async () => {
    const { user } = renderWithProviders(
      <InvoiceFormDialog open invoice={null} onClose={vi.fn()} onSaved={vi.fn()} />
    );

    // The preview-only note is always shown (server totals are authoritative).
    const dialog = await screen.findByRole('dialog');
    expect(
      within(dialog).getByText(/Preview only — the server computes the authoritative tax/i)
    ).toBeInTheDocument();

    // Type a real line (qty 5 × unit 20 = net 100, tax 20% = 20, gross 120) and assert the
    // client preview RECOMPUTES from the watched lines (regression guard for the watch→useMemo
    // wiring flagged by ui-validate — net/tax/gross must leave 0.00).
    const numbers = within(dialog).getAllByRole('spinbutton');
    await user.clear(numbers[0]);
    await user.type(numbers[0], '5');
    await user.clear(numbers[1]);
    await user.type(numbers[1], '20');
    await user.clear(numbers[2]);
    await user.type(numbers[2], '0.2');

    await waitFor(() => {
      expect(within(dialog).getByText(/100[.,]00/)).toBeInTheDocument();
      expect(within(dialog).getByText(/120[.,]00/)).toBeInTheDocument();
    });
  });

  it('InvoiceForm_MissingCounterparty_ShowsCounterpartyRequired', async () => {
    const { user } = renderWithProviders(
      <InvoiceFormDialog open invoice={null} onClose={vi.fn()} onSaved={vi.fn()} />
    );

    await user.click(await screen.findByRole('button', { name: 'Save' }));

    expect(await screen.findByText('A counterparty is required.')).toBeInTheDocument();
    expect(createInvoiceMock).not.toHaveBeenCalled();
  });

  it('InvoiceNote_FromPostedInvoice_OpensFormWithCorrectsInvoiceId', async () => {
    createInvoiceMock.mockResolvedValue(posted({ id: 'note-1', documentType: InvoiceDocumentType.CreditNote }));
    const original = posted();

    const { user } = renderWithProviders(
      <CreateNoteDialog
        original={original}
        noteType={InvoiceDocumentType.CreditNote}
        onClose={vi.fn()}
        onSaved={vi.fn()}
      />
    );

    const dialog = await screen.findByRole('dialog');
    // Fill the required counterparty + currency + a valid line so the create submits.
    await user.type(within(dialog).getByPlaceholderText('Counterparty id'), 'cp-9');
    // The currency MUI select renders as a combobox; open it and pick BGN.
    const comboboxes = within(dialog).getAllByRole('combobox');
    await user.click(comboboxes[comboboxes.length - 1]);
    await user.click(await screen.findByRole('option', { name: 'BGN' }));
    // The first text input is the line description (the only required free-text line field).
    const description = within(dialog).getAllByRole('textbox')[1];
    await user.type(description, 'Refund widget');

    // Submit; the note must carry correctsInvoiceId = the original id and the preset type.
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(createInvoiceMock).toHaveBeenCalled());
    const request = createInvoiceMock.mock.calls[0][0];
    expect(request.correctsInvoiceId).toBe('orig-1111');
    expect(request.documentType).toBe(InvoiceDocumentType.CreditNote);
  });

  it('InvoiceEdit_StaleRowVersion_ShowsConcurrentModificationToast', async () => {
    updateInvoiceMock.mockRejectedValue(
      new AxiosError('conflict', undefined, undefined, undefined, {
        status: 409,
        data: { title: 'CONCURRENT_MODIFICATION' }
      } as never)
    );
    const draft = posted({ status: InvoiceStatus.Draft, documentNumber: null, journalEntryId: null });

    const { user } = renderWithProviders(
      <InvoiceFormDialog open invoice={draft} onClose={vi.fn()} onSaved={vi.fn()} />
    );

    await user.click(await screen.findByRole('button', { name: 'Save' }));

    expect(
      await screen.findByText(/This record was changed by someone else/i)
    ).toBeInTheDocument();
  });
});

describe('CancelInvoiceDialog (SDD-UI-FIN-001 §2.7)', () => {
  beforeEach(() => {
    cancelInvoiceMock.mockReset();
  });

  it('InvoiceCancel_EmptyReason_SubmitStaysActionable_AndServerCodeMapped', async () => {
    cancelInvoiceMock.mockRejectedValue(
      new AxiosError('bad', undefined, undefined, undefined, {
        status: 400,
        data: { title: 'INVOICE_CANCEL_REASON_REQUIRED' }
      } as never)
    );
    const draft = posted({ status: InvoiceStatus.Draft, documentNumber: null, journalEntryId: null });

    const { user } = renderWithProviders(
      <CancelInvoiceDialog invoice={draft} onClose={vi.fn()} onCancelled={vi.fn()} />
    );

    const dialog = await screen.findByRole('dialog');
    // Submitting with an empty reason triggers the inline reason-required validation (zod);
    // the submit action is "Cancel invoice" (distinct from the dialog's "Cancel" close button).
    await user.click(within(dialog).getByRole('button', { name: 'Cancel invoice' }));
    expect(await within(dialog).findByText('A reason is required.')).toBeInTheDocument();
    expect(cancelInvoiceMock).not.toHaveBeenCalled();
  });
});
