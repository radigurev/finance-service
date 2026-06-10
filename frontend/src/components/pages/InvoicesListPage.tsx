import { useEffect, useMemo, useState } from 'react';
import { Box, Tooltip } from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import EditIcon from '@mui/icons-material/Edit';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import PublishIcon from '@mui/icons-material/Publish';
import BlockIcon from '@mui/icons-material/Block';
import RemoveCircleOutlineIcon from '@mui/icons-material/RemoveCircleOutline';
import AddCircleOutlineIcon from '@mui/icons-material/AddCircleOutline';
import LinkIcon from '@mui/icons-material/Link';
import { type GridColDef, type GridPaginationModel, type GridSortModel } from '@mui/x-data-grid';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { ListPageTemplate } from '@/components/templates';
import {
  DataTable,
  ledgerMonoColumn,
  InvoiceFormDialog,
  CancelInvoiceDialog,
  CreateNoteDialog
} from '@/components/organisms';
import { FilterBar, ConfirmDialog } from '@/components/molecules';
import { AppButton, CodeText, MoneyText, StatusDot } from '@/components/atoms';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';
import { MAX_PAGE_SIZE, type FilterRequest } from '@/shared/api/paging';
import { searchInvoices } from '@/features/invoices/api';
import { useInvoiceMutations } from '@/features/invoices/useInvoiceMutations';
import {
  InvoiceDocumentType,
  InvoiceStatus,
  POSTING_PENDING,
  directionLabelKey,
  displayStatusKey,
  documentTypeLabelKey,
  type InvoiceDto
} from '@/features/invoices/types';

/** Default page size for the invoices grid (≤ the backend cap of 200, SDD-INFRA-005). */
const DEFAULT_PAGE_SIZE = 50;

/** Formats an ISO 8601 timestamp as a `yyyy-MM-dd` calendar date for display. */
function formatDate(value: string): string {
  return value.slice(0, 10);
}

/** Maps the displayed status (incl. the posting-pending affordance) to a {@link StatusDot} tone. */
function statusTone(invoice: InvoiceDto): 'positive' | 'neutral' | 'warning' | 'danger' {
  if (invoice.status === InvoiceStatus.Posted) {
    return 'positive';
  }
  if (invoice.status === InvoiceStatus.Cancelled || invoice.status === InvoiceStatus.Reversed) {
    return 'danger';
  }
  if (invoice.status === InvoiceStatus.Confirmed) {
    return 'warning';
  }
  return 'neutral';
}

/**
 * Invoices listing (SDD-UI-FIN-001 §2.1; SDD-INV-001 §2.10). Server-side paging / sorting / search
 * via the GenericFiltering contract (default order `IssueDate` desc); create + edit a DRAFT through
 * {@link InvoiceFormDialog}; confirm/post/cancel/delete and credit/debit-note correction through
 * status-gated dialogs. Row actions are gated by status so the UI never offers an illegal
 * transition (§2.3): Draft → edit/confirm/cancel/delete; Confirmed → post/cancel (or "posting…");
 * Posted → create credit/debit note. Warehouse-created drafts surface in the same list with their
 * source origin when the DTO carries it (§2.10). Failures surface via
 * `notification.error(getApiErrorMessage(...))`.
 */
export function InvoicesListPage() {
  const { t } = useTranslation();
  const { remove, confirm, post, isSaving } = useInvoiceMutations();

  const [search, setSearch] = useState('');
  const [paginationModel, setPaginationModel] = useState<GridPaginationModel>({
    page: 0,
    pageSize: DEFAULT_PAGE_SIZE
  });
  const [sortModel, setSortModel] = useState<GridSortModel>([{ field: 'issueDate', sort: 'desc' }]);

  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<InvoiceDto | null>(null);
  const [deleting, setDeleting] = useState<InvoiceDto | null>(null);
  const [confirming, setConfirming] = useState<InvoiceDto | null>(null);
  const [posting, setPosting] = useState<InvoiceDto | null>(null);
  const [cancelling, setCancelling] = useState<InvoiceDto | null>(null);
  const [noting, setNoting] = useState<{
    original: InvoiceDto;
    noteType: InvoiceDocumentType.CreditNote | InvoiceDocumentType.DebitNote;
  } | null>(null);

  const filterRequest = useMemo<FilterRequest>(
    () => ({
      page: paginationModel.page + 1,
      pageSize: Math.min(paginationModel.pageSize, MAX_PAGE_SIZE),
      search: search || undefined,
      sort: sortModel
        .filter((s) => s.sort)
        .map((s) => ({ field: s.field, direction: s.sort === 'desc' ? 'desc' : 'asc' }))
    }),
    [paginationModel, sortModel, search]
  );

  const { data, isFetching, error } = useQuery({
    queryKey: ['invoices', filterRequest],
    queryFn: () => searchInvoices(filterRequest),
    placeholderData: (prev) => prev
  });

  useEffect(() => {
    if (error) {
      notification.error(getApiErrorMessage(error, t));
    }
  }, [error, t]);

  function openCreate() {
    setEditing(null);
    setFormOpen(true);
  }

  function openEdit(invoice: InvoiceDto) {
    setEditing(invoice);
    setFormOpen(true);
  }

  function handleSearchChange(term: string) {
    setSearch(term);
    setPaginationModel((prev) => ({ ...prev, page: 0 }));
  }

  async function confirmDelete() {
    if (!deleting) {
      return;
    }
    const ok = await remove(deleting.id);
    if (ok) {
      setDeleting(null);
    }
  }

  async function confirmConfirm() {
    if (!confirming) {
      return;
    }
    const result = await confirm({ id: confirming.id, rowVersion: confirming.rowVersion });
    if (result) {
      setConfirming(null);
    }
  }

  async function confirmPost() {
    if (!posting) {
      return;
    }
    const result = await post({ id: posting.id, rowVersion: posting.rowVersion });
    if (result) {
      setPosting(null);
    }
  }

  const columns = useMemo<GridColDef<InvoiceDto>[]>(
    () => [
      {
        field: 'documentNumber',
        headerName: t('invoices.documentNumber'),
        width: 150,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        renderCell: (params) => (
          <Box sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.5 }}>
            <CodeText>{params.row.documentNumber ?? '—'}</CodeText>
            {params.row.sourceDocumentType ? (
              <Tooltip
                title={t('invoices.sourceOrigin', {
                  type: params.row.sourceDocumentType,
                  id: params.row.sourceDocumentId ?? ''
                })}
              >
                <LinkIcon fontSize="inherit" sx={{ fontSize: '0.875rem', color: 'text.secondary' }} />
              </Tooltip>
            ) : null}
          </Box>
        )
      },
      {
        field: 'documentType',
        headerName: t('invoices.documentType'),
        width: 150,
        renderCell: (params) => t(documentTypeLabelKey(params.row.documentType))
      },
      {
        field: 'direction',
        headerName: t('invoices.direction'),
        width: 90,
        renderCell: (params) => t(directionLabelKey(params.row.direction))
      },
      {
        field: 'counterpartyId',
        headerName: t('invoices.counterparty'),
        flex: 1,
        minWidth: 200,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        sortable: false,
        renderCell: (params) => <CodeText>{params.row.counterpartyId}</CodeText>
      },
      {
        field: 'currencyCode',
        headerName: t('invoices.currency'),
        width: 100,
        ...ledgerMonoColumn,
        renderCell: (params) => <CodeText>{params.row.currencyCode}</CodeText>
      },
      {
        field: 'issueDate',
        headerName: t('invoices.issueDate'),
        width: 130,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        renderCell: (params) => <CodeText>{formatDate(params.row.issueDate)}</CodeText>
      },
      {
        field: 'dueDate',
        headerName: t('invoices.dueDate'),
        width: 130,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        renderCell: (params) => <CodeText>{formatDate(params.row.dueDate)}</CodeText>
      },
      {
        field: 'status',
        headerName: t('invoices.status'),
        width: 130,
        sortable: true,
        renderCell: (params) => (
          <StatusDot tone={statusTone(params.row)} label={t(displayStatusKey(params.row))} />
        )
      },
      {
        field: 'grossTotal',
        headerName: t('invoices.grossTotal'),
        width: 150,
        ...ledgerMonoColumn,
        renderCell: (params) => (
          <MoneyText amount={params.row.grossTotal} currency={params.row.currencyCode} />
        )
      },
      {
        field: 'actions',
        headerName: '',
        width: 196,
        sortable: false,
        align: 'right',
        renderCell: (params) => renderActions(params.row)
      }
    ],
    [t]
  );

  function renderActions(invoice: InvoiceDto) {
    const isDraft = invoice.status === InvoiceStatus.Draft;
    const isConfirmed = invoice.status === InvoiceStatus.Confirmed;
    const isPosted = invoice.status === InvoiceStatus.Posted;
    const isPostingPending = displayStatusKey(invoice).endsWith(POSTING_PENDING);

    return (
      <Box sx={{ display: 'flex', justifyContent: 'flex-end' }}>
        {isDraft ? (
          <>
            <AppButton
              variant="text"
              size="small"
              aria-label={t('invoices.edit')}
              onClick={() => openEdit(invoice)}
              sx={{ minWidth: 0, px: 1 }}
            >
              <EditIcon fontSize="small" />
            </AppButton>
            <AppButton
              variant="text"
              size="small"
              aria-label={t('invoices.confirm')}
              onClick={() => setConfirming(invoice)}
              sx={{ minWidth: 0, px: 1 }}
            >
              <CheckCircleOutlineIcon fontSize="small" />
            </AppButton>
            <AppButton
              variant="text"
              size="small"
              aria-label={t('invoices.cancel')}
              onClick={() => setCancelling(invoice)}
              sx={{ minWidth: 0, px: 1 }}
            >
              <BlockIcon fontSize="small" />
            </AppButton>
            <AppButton
              variant="text"
              size="small"
              color="error"
              aria-label={t('invoices.delete')}
              onClick={() => setDeleting(invoice)}
              sx={{ minWidth: 0, px: 1 }}
            >
              <DeleteOutlineIcon fontSize="small" />
            </AppButton>
          </>
        ) : null}

        {isConfirmed ? (
          <>
            <Tooltip title={isPostingPending ? t('invoices.postingPendingHint') : t('invoices.post')}>
              <span>
                <AppButton
                  variant="text"
                  size="small"
                  aria-label={t('invoices.post')}
                  onClick={() => setPosting(invoice)}
                  sx={{ minWidth: 0, px: 1 }}
                >
                  <PublishIcon fontSize="small" />
                </AppButton>
              </span>
            </Tooltip>
            <AppButton
              variant="text"
              size="small"
              aria-label={t('invoices.cancel')}
              onClick={() => setCancelling(invoice)}
              sx={{ minWidth: 0, px: 1 }}
            >
              <BlockIcon fontSize="small" />
            </AppButton>
          </>
        ) : null}

        {isPosted ? (
          <>
            <AppButton
              variant="text"
              size="small"
              aria-label={t('invoices.createCreditNote')}
              onClick={() => setNoting({ original: invoice, noteType: InvoiceDocumentType.CreditNote })}
              sx={{ minWidth: 0, px: 1 }}
            >
              <RemoveCircleOutlineIcon fontSize="small" />
            </AppButton>
            <AppButton
              variant="text"
              size="small"
              aria-label={t('invoices.createDebitNote')}
              onClick={() => setNoting({ original: invoice, noteType: InvoiceDocumentType.DebitNote })}
              sx={{ minWidth: 0, px: 1 }}
            >
              <AddCircleOutlineIcon fontSize="small" />
            </AppButton>
          </>
        ) : null}
      </Box>
    );
  }

  return (
    <ListPageTemplate
      overline={t('nav.section')}
      title={t('invoices.title')}
      actions={
        <AppButton variant="contained" startIcon={<AddIcon />} onClick={openCreate}>
          {t('invoices.newInvoice')}
        </AppButton>
      }
      toolbar={
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap', width: '100%' }}>
          <FilterBar
            value={search}
            onSearchChange={handleSearchChange}
            placeholder={t('invoices.searchPlaceholder')}
          />
        </Box>
      }
    >
      <DataTable<InvoiceDto>
        rows={data?.items ?? []}
        columns={columns}
        getRowId={(row) => row.id}
        loading={isFetching}
        rowCount={data?.totalCount ?? 0}
        paginationModel={paginationModel}
        onPaginationModelChange={setPaginationModel}
        sortModel={sortModel}
        onSortModelChange={setSortModel}
        emptyTitle={t('invoices.empty')}
        emptyDescription={t('invoices.emptyHint')}
        emptyAction={
          <AppButton variant="outlined" startIcon={<AddIcon />} onClick={openCreate}>
            {t('invoices.newInvoice')}
          </AppButton>
        }
      />

      <InvoiceFormDialog
        open={formOpen}
        invoice={editing}
        onClose={() => setFormOpen(false)}
        onSaved={() => setFormOpen(false)}
      />

      <ConfirmDialog
        open={confirming !== null}
        title={t('invoices.confirmTitle')}
        message={t('invoices.confirmMessage')}
        confirmLabel={t('invoices.confirm')}
        busy={isSaving}
        onConfirm={confirmConfirm}
        onCancel={() => setConfirming(null)}
      />

      <ConfirmDialog
        open={posting !== null}
        title={t('invoices.postTitle')}
        message={t('invoices.postMessage', { number: posting?.documentNumber ?? '' })}
        confirmLabel={t('invoices.post')}
        busy={isSaving}
        onConfirm={confirmPost}
        onCancel={() => setPosting(null)}
      />

      <CancelInvoiceDialog
        invoice={cancelling}
        onClose={() => setCancelling(null)}
        onCancelled={() => setCancelling(null)}
      />

      <ConfirmDialog
        open={deleting !== null}
        title={t('invoices.deleteTitle')}
        message={t('invoices.deleteMessage')}
        confirmLabel={t('invoices.delete')}
        destructive
        busy={isSaving}
        onConfirm={confirmDelete}
        onCancel={() => setDeleting(null)}
      />

      <CreateNoteDialog
        original={noting?.original ?? null}
        noteType={noting?.noteType ?? InvoiceDocumentType.CreditNote}
        onClose={() => setNoting(null)}
        onSaved={() => setNoting(null)}
      />
    </ListPageTemplate>
  );
}
