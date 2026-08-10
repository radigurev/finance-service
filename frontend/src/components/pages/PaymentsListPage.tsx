import { useEffect, useMemo, useState } from 'react';
import { Box, Tooltip } from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import EditIcon from '@mui/icons-material/Edit';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import PublishIcon from '@mui/icons-material/Publish';
import BlockIcon from '@mui/icons-material/Block';
import UndoIcon from '@mui/icons-material/Undo';
import PlaylistAddIcon from '@mui/icons-material/PlaylistAdd';
import ReceiptLongIcon from '@mui/icons-material/ReceiptLong';
import { type GridColDef, type GridPaginationModel, type GridSortModel } from '@mui/x-data-grid';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { ListPageTemplate } from '@/components/templates';
import {
  DataTable,
  ledgerMonoColumn,
  PaymentFormDialog,
  CancelPaymentDialog,
  ReversePaymentDialog,
  PaymentAllocationsDialog,
  AllocatePaymentDialog
} from '@/components/organisms';
import { FilterBar, ConfirmDialog, ForbiddenState } from '@/components/molecules';
import { AppButton, CodeText, MoneyText, StatusDot } from '@/components/atoms';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage, isForbiddenError } from '@/shared/utils/getApiErrorMessage';
import { MAX_PAGE_SIZE, type FilterRequest } from '@/shared/api/paging';
import { searchPayments } from '@/features/payments/api';
import { usePaymentMutations } from '@/features/payments/usePaymentMutations';
import {
  PaymentStatus,
  directionLabelKey,
  displayDocumentNumber,
  displayStatusKey,
  documentTypeLabelKey,
  isPostingPending,
  methodLabelKey,
  type AllocatePaymentResultDto,
  type DeallocatePaymentResultDto,
  type PaymentDto
} from '@/features/payments/types';

/** Default page size for the payments grid (≤ the backend cap of 200, SDD-INFRA-005). */
const DEFAULT_PAGE_SIZE = 50;

/**
 * Width of the `UNALLOCATED` column. It hosts a figure AND the "unapplied" badge side by side; 160 was
 * measured too narrow (a four-digit amount produced `scrollWidth` 169 against `clientWidth` 160 and the
 * badge rendered as `UNAPPLIED…`). Exported so the regression test asserts the rendered column width
 * rather than re-deriving the number.
 */
export const UNALLOCATED_COLUMN_WIDTH = 210;

/** Formats an ISO 8601 timestamp as a `yyyy-MM-dd` calendar date for display. */
function formatDate(value: string): string {
  return value.slice(0, 10);
}

/** Maps the displayed status (incl. the posting-pending affordance) to a {@link StatusDot} tone. */
function statusTone(payment: PaymentDto): 'positive' | 'neutral' | 'warning' | 'danger' {
  if (payment.status === PaymentStatus.Posted) {
    return 'positive';
  }
  if (payment.status === PaymentStatus.Cancelled || payment.status === PaymentStatus.Reversed) {
    return 'danger';
  }
  if (payment.status === PaymentStatus.Confirmed) {
    return 'warning';
  }
  return 'neutral';
}

/**
 * Payments listing (SDD-UI-FIN-002 §2.1–§2.9; SDD-PAY-001). Server-side paging / sorting / search via
 * the GenericFiltering contract with a default order of `PaymentDate` DESC; create + edit a DRAFT
 * through {@link PaymentFormDialog}; confirm / post / cancel / reverse / delete and the allocation
 * sub-surface through status-gated dialogs.
 *
 * Row actions are gated by STATUS so the UI never offers a transition the backend would reject (§2.4).
 * The legal set is NARROWER than the invoice one:
 *
 * - `Draft` → Edit, Confirm, **Cancel**, Delete. NOT Post, Reverse, or Allocate (allocation requires
 *   `Confirmed`/`Posted`).
 * - `Confirmed` → Post (or the quiet "posting…" affordance) and the allocation actions.
 *   **NO Cancel** — `Confirmed → Cancelled` was deliberately removed from `AllowedNextStates`
 *   (§1.4 trap 3); a confirmed payment is completed to `Posted` and then reversed.
 * - `Posted` → Reverse (disabled while `allocatedAmount > 0`, §1.4 trap 12) and the allocation actions.
 * - `Cancelled` / `Reversed` → nothing; terminal.
 *
 * Only the CLOSED backend opt-in surface is offered for filter/sort: `CounterpartyId` is
 * `[Filterable]`-only so its column exposes no sort, `DocumentNumber` is the SOLE `[Searchable]`
 * property (which the search placeholder says), and the derived allocation figures are not sortable at
 * all (§2.1). A `Draft` row — and a `Cancelled` row, forever — renders `—` for its document number
 * (§1.4 traps 4/5). `rowVersion` is re-seeded from every allocate/deallocate response, because those
 * writes increment it and a token captured from the list query goes stale immediately (§1.4 trap 11).
 * A `403` renders the editorial forbidden state rather than a raw status or a crash toast (§2.17).
 */
export function PaymentsListPage() {
  const { t } = useTranslation();
  const { remove, confirm, post, isSaving } = usePaymentMutations();

  const [search, setSearch] = useState('');
  const [paginationModel, setPaginationModel] = useState<GridPaginationModel>({
    page: 0,
    pageSize: DEFAULT_PAGE_SIZE
  });
  const [sortModel, setSortModel] = useState<GridSortModel>([
    { field: 'paymentDate', sort: 'desc' }
  ]);

  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<PaymentDto | null>(null);
  const [deleting, setDeleting] = useState<PaymentDto | null>(null);
  const [confirming, setConfirming] = useState<PaymentDto | null>(null);
  const [posting, setPosting] = useState<PaymentDto | null>(null);
  const [cancelling, setCancelling] = useState<PaymentDto | null>(null);
  const [reversing, setReversing] = useState<PaymentDto | null>(null);
  const [viewingAllocations, setViewingAllocations] = useState<PaymentDto | null>(null);
  const [allocating, setAllocating] = useState<PaymentDto | null>(null);

  /**
   * Fresh `rowVersion` tokens harvested from allocate/deallocate responses (§1.4 trap 11). Allocation
   * increments the payment's `RowVersion`, so the token from the list query is stale the moment a
   * match is made; chaining writes off the response avoids a guaranteed `CONCURRENT_MODIFICATION`
   * without needing a follow-up read.
   */
  const [rowVersions, setRowVersions] = useState<Record<string, string>>({});

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
    queryKey: ['payments', filterRequest],
    queryFn: () => searchPayments(filterRequest),
    placeholderData: (prev) => prev
  });

  const forbidden: boolean = isForbiddenError(error);

  useEffect(() => {
    if (error && !isForbiddenError(error)) {
      notification.error(getApiErrorMessage(error, t));
    }
  }, [error, t]);

  function effectiveRowVersion(payment: PaymentDto): string {
    return rowVersions[payment.id] ?? payment.rowVersion;
  }

  function seedRowVersion(paymentId: string, rowVersion: string) {
    setRowVersions((prev) => ({ ...prev, [paymentId]: rowVersion }));
  }

  function openCreate() {
    setEditing(null);
    setFormOpen(true);
  }

  function openEdit(payment: PaymentDto) {
    setEditing(payment);
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
    const result = await confirm({
      id: confirming.id,
      rowVersion: effectiveRowVersion(confirming)
    });
    if (result) {
      setConfirming(null);
    }
  }

  /**
   * Completes the Confirm→Post handshake. A `PAYMENT_POSTING_PENDING` answer is NOT a failure: the
   * call re-enqueued `PaymentConfirmedEvent` and the hook surfaces it as an informational
   * "retry queued" toast. The dialog therefore closes either way and the Post action stays available
   * so the operator may re-drive it — repeated retries are bounded in effect (§1.4 trap 6, §2.7).
   */
  async function confirmPost() {
    if (!posting) {
      return;
    }
    await post({ id: posting.id, rowVersion: effectiveRowVersion(posting) });
    setPosting(null);
  }

  function handleAllocated(result: AllocatePaymentResultDto) {
    seedRowVersion(result.paymentId, result.rowVersion);
    setAllocating(null);
  }

  function handleDeallocated(result: DeallocatePaymentResultDto) {
    seedRowVersion(result.paymentId, result.rowVersion);
  }

  const columns = useMemo<GridColDef<PaymentDto>[]>(
    () => [
      {
        field: 'documentNumber',
        headerName: t('payments.documentNumber'),
        width: 150,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        renderCell: (params) => <CodeText>{displayDocumentNumber(params.row)}</CodeText>
      },
      {
        field: 'documentType',
        headerName: t('payments.documentType'),
        width: 150,
        renderCell: (params) => t(documentTypeLabelKey(params.row.documentType))
      },
      {
        field: 'direction',
        headerName: t('payments.direction'),
        width: 80,
        renderCell: (params) => t(directionLabelKey(params.row.direction))
      },
      {
        field: 'method',
        headerName: t('payments.method'),
        width: 130,
        renderCell: (params) => t(methodLabelKey(params.row.method))
      },
      {
        field: 'counterpartyId',
        headerName: t('payments.counterparty'),
        flex: 1,
        minWidth: 200,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        sortable: false,
        renderCell: (params) => (
          // The tooltip anchors to a span: MUI Tooltip needs a ref-holding element, and the mono
          // atoms are plain function components.
          <Tooltip title={params.row.counterpartyId}>
            <span>
              <CodeText>{params.row.counterpartyId}</CodeText>
            </span>
          </Tooltip>
        )
      },
      {
        field: 'currencyCode',
        headerName: t('payments.currency'),
        width: 90,
        ...ledgerMonoColumn,
        renderCell: (params) => <CodeText>{params.row.currencyCode}</CodeText>
      },
      {
        field: 'amount',
        headerName: t('payments.amount'),
        width: 150,
        ...ledgerMonoColumn,
        renderCell: (params) => (
          <MoneyText amount={params.row.amount} currency={params.row.currencyCode} />
        )
      },
      {
        field: 'allocatedAmount',
        headerName: t('payments.allocatedAmount'),
        width: 140,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) => <MoneyText amount={params.row.allocatedAmount} />
      },
      {
        field: 'unallocatedAmount',
        headerName: t('payments.unallocatedAmount'),
        // The cell carries a grouped figure AND the "unapplied" badge on one line, so it needs room
        // for both: at 160 a four-digit amount already overflowed (scrollWidth 169) and the badge was
        // truncated to `UNAPPLIED…`. Sized for a six-digit grouped amount plus the LONGER of the two
        // locales' badges (BG `НЕУСВОЕНО` is wider than EN `UNAPPLIED` at the same tracking), and the
        // badge is kept on one line so it can never wrap into the row height at compact density.
        width: UNALLOCATED_COLUMN_WIDTH,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) =>
          params.row.unallocatedAmount > 0 ? (
            <Tooltip title={t('payments.unapplied')}>
              <Box
                sx={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: 0.75,
                  whiteSpace: 'nowrap'
                }}
              >
                <MoneyText amount={params.row.unallocatedAmount} />
                <Box
                  component="span"
                  sx={{
                    fontSize: '0.6875rem',
                    letterSpacing: '0.08em',
                    textTransform: 'uppercase',
                    whiteSpace: 'nowrap',
                    color: 'text.secondary'
                  }}
                >
                  {t('payments.unapplied')}
                </Box>
              </Box>
            </Tooltip>
          ) : (
            <MoneyText amount={params.row.unallocatedAmount} />
          )
      },
      {
        field: 'paymentDate',
        headerName: t('payments.paymentDate'),
        width: 130,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        renderCell: (params) => <CodeText>{formatDate(params.row.paymentDate)}</CodeText>
      },
      {
        field: 'status',
        headerName: t('payments.status'),
        width: 130,
        sortable: true,
        renderCell: (params) => (
          <StatusDot tone={statusTone(params.row)} label={t(displayStatusKey(params.row))} />
        )
      },
      {
        field: 'actions',
        headerName: '',
        width: 200,
        sortable: false,
        align: 'right',
        renderCell: (params) => renderActions(params.row)
      }
    ],
    [t]
  );

  function renderActions(payment: PaymentDto) {
    const isDraft: boolean = payment.status === PaymentStatus.Draft;
    const isConfirmed: boolean = payment.status === PaymentStatus.Confirmed;
    const isPosted: boolean = payment.status === PaymentStatus.Posted;
    const pendingPost: boolean = isPostingPending(payment);
    const reverseBlocked: boolean = payment.allocatedAmount > 0;

    return (
      <Box sx={{ display: 'flex', justifyContent: 'flex-end' }}>
        {isDraft ? (
          <>
            <AppButton
              variant="text"
              size="small"
              aria-label={t('payments.edit')}
              onClick={() => openEdit(payment)}
              sx={{ minWidth: 0, px: 1 }}
            >
              <EditIcon fontSize="small" />
            </AppButton>
            <AppButton
              variant="text"
              size="small"
              aria-label={t('payments.confirm')}
              onClick={() => setConfirming(payment)}
              sx={{ minWidth: 0, px: 1 }}
            >
              <CheckCircleOutlineIcon fontSize="small" />
            </AppButton>
            <AppButton
              variant="text"
              size="small"
              aria-label={t('payments.cancel')}
              onClick={() => setCancelling(payment)}
              sx={{ minWidth: 0, px: 1 }}
            >
              <BlockIcon fontSize="small" />
            </AppButton>
            <AppButton
              variant="text"
              size="small"
              color="error"
              aria-label={t('payments.delete')}
              onClick={() => setDeleting(payment)}
              sx={{ minWidth: 0, px: 1 }}
            >
              <DeleteOutlineIcon fontSize="small" />
            </AppButton>
          </>
        ) : null}

        {isConfirmed ? (
          <Tooltip title={pendingPost ? t('payments.postingPendingHint') : t('payments.post')}>
            <span>
              <AppButton
                variant="text"
                size="small"
                aria-label={t('payments.post')}
                onClick={() => setPosting(payment)}
                sx={{ minWidth: 0, px: 1 }}
              >
                <PublishIcon fontSize="small" />
              </AppButton>
            </span>
          </Tooltip>
        ) : null}

        {isPosted ? (
          <Tooltip
            title={reverseBlocked ? t('payments.reverseBlockedByAllocations') : t('payments.reverse')}
          >
            <span>
              <AppButton
                variant="text"
                size="small"
                color="error"
                aria-label={t('payments.reverse')}
                disabled={reverseBlocked}
                onClick={() => setReversing(payment)}
                sx={{ minWidth: 0, px: 1 }}
              >
                <UndoIcon fontSize="small" />
              </AppButton>
            </span>
          </Tooltip>
        ) : null}

        {isConfirmed || isPosted ? (
          <>
            <AppButton
              variant="text"
              size="small"
              aria-label={t('payments.viewAllocations')}
              onClick={() => setViewingAllocations(payment)}
              sx={{ minWidth: 0, px: 1 }}
            >
              <ReceiptLongIcon fontSize="small" />
            </AppButton>
            <AppButton
              variant="text"
              size="small"
              aria-label={t('payments.allocate')}
              onClick={() => setAllocating(payment)}
              sx={{ minWidth: 0, px: 1 }}
            >
              <PlaylistAddIcon fontSize="small" />
            </AppButton>
          </>
        ) : null}
      </Box>
    );
  }

  return (
    <ListPageTemplate
      overline={t('nav.section')}
      title={t('payments.title')}
      actions={
        <AppButton variant="contained" startIcon={<AddIcon />} onClick={openCreate}>
          {t('payments.newPayment')}
        </AppButton>
      }
      toolbar={
        forbidden ? undefined : (
          <Box
            sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap', width: '100%' }}
          >
            <FilterBar
              value={search}
              onSearchChange={handleSearchChange}
              placeholder={t('payments.searchPlaceholder')}
            />
          </Box>
        )
      }
    >
      {forbidden ? (
        <ForbiddenState title={t('payments.forbidden')} description={t('payments.forbiddenHint')} />
      ) : (
        <DataTable<PaymentDto>
          rows={data?.items ?? []}
          columns={columns}
          getRowId={(row) => row.id}
          loading={isFetching}
          rowCount={data?.totalCount ?? 0}
          paginationModel={paginationModel}
          onPaginationModelChange={setPaginationModel}
          sortModel={sortModel}
          onSortModelChange={setSortModel}
          emptyTitle={t('payments.empty')}
          emptyDescription={t('payments.emptyHint')}
          emptyAction={
            <AppButton variant="outlined" startIcon={<AddIcon />} onClick={openCreate}>
              {t('payments.newPayment')}
            </AppButton>
          }
        />
      )}

      <PaymentFormDialog
        open={formOpen}
        payment={editing}
        onClose={() => setFormOpen(false)}
        onSaved={() => setFormOpen(false)}
      />

      <ConfirmDialog
        open={confirming !== null}
        title={t('payments.confirmTitle')}
        message={t('payments.confirmMessage')}
        confirmLabel={t('payments.confirm')}
        busy={isSaving}
        onConfirm={confirmConfirm}
        onCancel={() => setConfirming(null)}
      />

      <ConfirmDialog
        open={posting !== null}
        title={t('payments.postTitle')}
        message={t('payments.postMessage', {
          number: posting ? displayDocumentNumber(posting) : ''
        })}
        confirmLabel={t('payments.post')}
        busy={isSaving}
        onConfirm={confirmPost}
        onCancel={() => setPosting(null)}
      />

      <CancelPaymentDialog
        payment={cancelling}
        rowVersion={cancelling ? effectiveRowVersion(cancelling) : undefined}
        onClose={() => setCancelling(null)}
        onCancelled={() => setCancelling(null)}
      />

      <ReversePaymentDialog
        payment={reversing}
        rowVersion={reversing ? effectiveRowVersion(reversing) : undefined}
        onClose={() => setReversing(null)}
        onReversed={() => setReversing(null)}
      />

      <ConfirmDialog
        open={deleting !== null}
        title={t('payments.deleteTitle')}
        message={t('payments.deleteMessage')}
        confirmLabel={t('payments.delete')}
        destructive
        busy={isSaving}
        onConfirm={confirmDelete}
        onCancel={() => setDeleting(null)}
      />

      <PaymentAllocationsDialog
        payment={viewingAllocations}
        rowVersion={viewingAllocations ? effectiveRowVersion(viewingAllocations) : undefined}
        onClose={() => setViewingAllocations(null)}
        onAllocate={() => {
          setAllocating(viewingAllocations);
          setViewingAllocations(null);
        }}
        onDeallocated={handleDeallocated}
      />

      <AllocatePaymentDialog
        payment={allocating}
        rowVersion={allocating ? effectiveRowVersion(allocating) : undefined}
        onClose={() => setAllocating(null)}
        onAllocated={handleAllocated}
      />
    </ListPageTemplate>
  );
}
