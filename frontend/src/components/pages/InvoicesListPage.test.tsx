import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import { AxiosError } from 'axios';
import { renderWithProviders } from '@/test/renderWithProviders';
import { InvoicesListPage } from './InvoicesListPage';
import { searchInvoices, confirmInvoice, postInvoice } from '@/features/invoices/api';
import {
  InvoiceDirection,
  InvoiceDocumentType,
  InvoiceStatus,
  type InvoiceDto
} from '@/features/invoices/types';
import type { PagedResult } from '@/shared/api/paging';

vi.mock('@/features/invoices/api');
// The create/edit dialog mounts a nomenclature-backed counterparty/currency picker; stub the
// hook so the page test makes no stray network calls.
vi.mock('@/shared/hooks/useNomenclature', () => ({
  useNomenclature: () => ({
    countries: [],
    currencies: [],
    isLoading: false,
    getStates: vi.fn(),
    getCities: vi.fn()
  })
}));

const searchInvoicesMock = vi.mocked(searchInvoices);
const confirmInvoiceMock = vi.mocked(confirmInvoice);
const postInvoiceMock = vi.mocked(postInvoice);

function paged(items: InvoiceDto[], over: Partial<PagedResult<InvoiceDto>> = {}): PagedResult<InvoiceDto> {
  return { items, totalCount: items.length, page: 1, pageSize: 50, ...over };
}

function invoice(over: Partial<InvoiceDto> = {}): InvoiceDto {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    documentNumber: 'SINV-2026-0001',
    documentType: InvoiceDocumentType.SaleInvoice,
    direction: InvoiceDirection.AR,
    status: InvoiceStatus.Posted,
    counterpartyId: '22222222-2222-2222-2222-222222222222',
    currencyCode: 'BGN',
    baseCurrencyCode: 'BGN',
    issueDate: '2026-06-01T00:00:00+00:00',
    dueDate: '2026-06-15T00:00:00+00:00',
    netTotal: 100,
    taxTotal: 20,
    grossTotal: 120,
    correctsInvoiceId: null,
    journalEntryId: '33333333-3333-3333-3333-333333333333',
    createdAt: '2026-06-01T00:00:00+00:00',
    confirmedAt: '2026-06-01T00:00:00+00:00',
    postedAt: '2026-06-01T00:00:00+00:00',
    lines: [],
    rowVersion: 'AAAA',
    ...over
  };
}

describe('InvoicesListPage (SDD-UI-FIN-001)', () => {
  beforeEach(() => {
    searchInvoicesMock.mockReset();
    confirmInvoiceMock.mockReset();
    postInvoiceMock.mockReset();
  });

  it('InvoicesList_Authenticated_RendersPagedResultEnvelope', async () => {
    searchInvoicesMock.mockResolvedValue(paged([invoice()], { totalCount: 1 }));

    renderWithProviders(<InvoicesListPage />);

    expect(await screen.findByText('Invoices')).toBeInTheDocument();
    expect(await screen.findByText('SINV-2026-0001')).toBeInTheDocument();
    // The query reads the envelope (items/totalCount), not a bare array.
    await waitFor(() => expect(searchInvoicesMock).toHaveBeenCalled());
  });

  it('InvoicesList_FilterSortPage_SendsFilterRequestParams_ServerSidePaging', async () => {
    searchInvoicesMock.mockResolvedValue(paged([invoice()]));

    renderWithProviders(<InvoicesListPage />);

    await waitFor(() => expect(searchInvoicesMock).toHaveBeenCalled());
    const request = searchInvoicesMock.mock.calls[0][0];
    expect(request.page).toBe(1);
    expect(request.pageSize).toBe(50);
    expect(request.pageSize).toBeLessThanOrEqual(200);
    expect(request.sort).toEqual([{ field: 'issueDate', direction: 'desc' }]);
  });

  it('InvoicesList_DraftRow_ShowsDashForDocumentNumber', async () => {
    searchInvoicesMock.mockResolvedValue(
      paged([invoice({ status: InvoiceStatus.Draft, documentNumber: null, journalEntryId: null })])
    );

    renderWithProviders(<InvoicesListPage />);

    expect(await screen.findByText('—')).toBeInTheDocument();
  });

  it('InvoicesList_ActionsGatedByStatus_DraftEditDeleteConfirm', async () => {
    searchInvoicesMock.mockResolvedValue(
      paged([invoice({ status: InvoiceStatus.Draft, documentNumber: null, journalEntryId: null })])
    );

    renderWithProviders(<InvoicesListPage />);

    expect(await screen.findByLabelText('Edit')).toBeInTheDocument();
    expect(screen.getByLabelText('Confirm')).toBeInTheDocument();
    expect(screen.getByLabelText('Delete')).toBeInTheDocument();
    expect(screen.queryByLabelText('Post')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Create credit note')).not.toBeInTheDocument();
  });

  it('InvoicesList_ActionsGatedByStatus_ConfirmedPostCancel', async () => {
    searchInvoicesMock.mockResolvedValue(paged([invoice({ status: InvoiceStatus.Confirmed })]));

    renderWithProviders(<InvoicesListPage />);

    expect(await screen.findByRole('button', { name: 'Post' })).toBeInTheDocument();
    expect(screen.getByLabelText('Cancel invoice')).toBeInTheDocument();
    expect(screen.queryByLabelText('Edit')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Delete')).not.toBeInTheDocument();
  });

  it('InvoicesImmutability_PostedRow_NoEditOrDelete_OnlyNotes', async () => {
    searchInvoicesMock.mockResolvedValue(paged([invoice({ status: InvoiceStatus.Posted })]));

    renderWithProviders(<InvoicesListPage />);

    expect(await screen.findByLabelText('Create credit note')).toBeInTheDocument();
    expect(screen.getByLabelText('Create debit note')).toBeInTheDocument();
    expect(screen.queryByLabelText('Edit')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Delete')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Cancel')).not.toBeInTheDocument();
  });

  it('InvoicesList_PostingPending_ShowsPostingAffordance', async () => {
    searchInvoicesMock.mockResolvedValue(
      paged([invoice({ status: InvoiceStatus.Confirmed, journalEntryId: null })])
    );

    renderWithProviders(<InvoicesListPage />);

    expect(await screen.findByText('Posting…')).toBeInTheDocument();
  });

  it('InvoicesList_WarehouseDraft_ShowsSourceOriginWhenPresent', async () => {
    searchInvoicesMock.mockResolvedValue(
      paged([
        invoice({
          status: InvoiceStatus.Draft,
          documentNumber: null,
          journalEntryId: null,
          sourceDocumentType: 'GoodsReceiptCompleted',
          sourceDocumentId: '44444444-4444-4444-4444-444444444444'
        })
      ])
    );

    renderWithProviders(<InvoicesListPage />);

    // The origin link affordance carries an accessible tooltip title; assert it is present.
    expect(
      await screen.findByLabelText(/System-created from GoodsReceiptCompleted/i)
    ).toBeInTheDocument();
  });

  it('InvoiceMutations_Confirm_NotDraft_ShowsInvoiceNotDraftToast', async () => {
    searchInvoicesMock.mockResolvedValue(paged([invoice({ status: InvoiceStatus.Draft, documentNumber: null, journalEntryId: null })]));
    confirmInvoiceMock.mockRejectedValue(
      new AxiosError('conflict', undefined, undefined, undefined, {
        status: 409,
        data: { title: 'INVOICE_NOT_DRAFT' }
      } as never)
    );

    const { user } = renderWithProviders(<InvoicesListPage />);

    await user.click(await screen.findByLabelText('Confirm'));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Confirm' }));

    expect(
      await screen.findByText('Only a draft invoice can be modified or confirmed.')
    ).toBeInTheDocument();
  });

  it('InvoiceMutations_Post_PostingNotLinked_ShowsInvoiceNotConfirmedToast', async () => {
    searchInvoicesMock.mockResolvedValue(paged([invoice({ status: InvoiceStatus.Confirmed, journalEntryId: null })]));
    postInvoiceMock.mockRejectedValue(
      new AxiosError('conflict', undefined, undefined, undefined, {
        status: 409,
        data: { title: 'INVOICE_NOT_CONFIRMED' }
      } as never)
    );

    const { user } = renderWithProviders(<InvoicesListPage />);

    await user.click(await screen.findByLabelText('Post'));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Post' }));

    expect(
      await screen.findByText('The invoice is not confirmed, or its posting is not yet linked.')
    ).toBeInTheDocument();
  });

  it('InvoiceError_UnmappedCode_FallsBackToGenericError', async () => {
    searchInvoicesMock.mockRejectedValue(
      new AxiosError('boom', undefined, undefined, undefined, {
        status: 500,
        data: { title: 'TOTALLY_UNKNOWN_CODE' }
      } as never)
    );

    renderWithProviders(<InvoicesListPage />);

    // The helper uses the ProblemDetails detail/title fallback only when present; an unmapped
    // title with no detail surfaces the code, never a raw i18n key path.
    expect(await screen.findByText('TOTALLY_UNKNOWN_CODE')).toBeInTheDocument();
    expect(screen.queryByText(/^errors\./)).not.toBeInTheDocument();
  });

  it('InvoiceError_QueryFailure_ShowsMappedNotRaw', async () => {
    searchInvoicesMock.mockRejectedValue(
      new AxiosError('boom', undefined, undefined, undefined, {
        status: 500,
        data: { title: 'GENERIC_ERROR' }
      } as never)
    );

    renderWithProviders(<InvoicesListPage />);

    expect(await screen.findByText(/something went wrong/i)).toBeInTheDocument();
  });
});
