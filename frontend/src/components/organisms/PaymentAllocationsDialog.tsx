import { useEffect, useMemo, useState } from 'react';
import {
  Box,
  Dialog,
  DialogActions,
  DialogContent,
  Stack,
  Tooltip,
  Typography
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import LinkOffIcon from '@mui/icons-material/LinkOff';
import { type GridColDef, type GridPaginationModel, type GridSortModel } from '@mui/x-data-grid';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { AppButton, AppTextField, CodeText, MoneyText } from '@/components/atoms';
import { FormField } from '@/components/molecules';
import { useLayoutStore } from '@/shared/stores/layout';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';
import { MAX_PAGE_SIZE, type FilterRequest } from '@/shared/api/paging';
import { serifFamily } from '@/shared/theme';
import { searchPaymentAllocations } from '@/features/payments/api';
import { useAllocationMutations } from '@/features/payments/useAllocationMutations';
import {
  PaymentStatus,
  settlementStatusLabelKey,
  type DeallocatePaymentResultDto,
  type PaymentAllocationDto,
  type PaymentDto
} from '@/features/payments/types';
import { DataTable, ledgerMonoColumn } from './DataTable';

interface PaymentAllocationsDialogProps {
  /** The payment whose allocation rows are shown; `null` keeps the dialog closed. */
  payment: PaymentDto | null;
  /** The freshest `rowVersion` for the payment (re-seeded after any allocate/deallocate). */
  rowVersion?: string;
  onClose: () => void;
  /** Asks the caller to open the allocate picker (rendered as a SIBLING dialog, never nested). */
  onAllocate: () => void;
  /** Hands back the deallocate result so the caller can re-seed the payment `rowVersion`. */
  onDeallocated: (result: DeallocatePaymentResultDto) => void;
}

/** Default page size for the allocations grid (within the 200 cap, SDD-INFRA-005). */
const DEFAULT_PAGE_SIZE = 25;

/** Formats an ISO 8601 timestamp as a `yyyy-MM-dd` calendar date for display. */
function formatDate(value: string | null): string {
  return value ? value.slice(0, 10) : '—';
}

/**
 * The per-payment allocation panel (SDD-UI-FIN-002 §2.10, §2.12; SDD-PAY-002 §2.6, §2.7). Lists the
 * payment's allocation rows as a paged, invoice-enriched envelope with a default order of
 * `AllocatedAt` descending, and releases one row at a time.
 *
 * Shipped-contract details that shape this panel:
 *
 * - **Filter/sort target only the opt-in surface** on `PaymentAllocation`: `InvoiceId` is
 *   `[Filterable]`-only so its column exposes no sort; `AllocatedAmount` and `AllocatedAt` are both
 *   filterable and sortable. NO `[Searchable]` property exists, so there is deliberately no search box.
 * - **`invoiceSettlementStatus` is the NUMERIC `SettlementStatus`** and is rendered from an i18n
 *   label. It is never re-derived client-side from `settledAmount` vs `grossTotal` — the server owns
 *   the single `SettlementStatusCalculator` (§2.10).
 * - **`realizedFxDifference` is INFORMATIONAL.** `IRealizedFxHandler` is wired to the inert
 *   `NoOpRealizedFxHandler` pending SDD-FIN-005, so the figure is labelled as such and is never
 *   presented as a posted GL amount. A zero value renders `0.00`, not blank (§1.6 gap 18).
 * - **A payment with NO allocations renders an EMPTY STATE with a quiet allocate action, never an
 *   error** — unapplied cash is a normal business state (§2.10).
 * - **Deallocate sends `rowVersion` and `reason` as QUERY parameters**, never a body (§1.4 trap 9),
 *   and the result re-seeds the payment `rowVersion` because allocation writes increment it
 *   (§1.4 trap 11). There is no in-place amount amendment in v1: a wrong amount is corrected by
 *   releasing and re-allocating (§1.6 gap 6), which the copy states.
 * - **A `Cancelled` / `Reversed` payment's rows are read-only history** — no release, no allocate
 *   (§2.4).
 */
export function PaymentAllocationsDialog({
  payment,
  rowVersion,
  onClose,
  onAllocate,
  onDeallocated
}: PaymentAllocationsDialogProps) {
  const { t } = useTranslation();
  const isCompact = useLayoutStore((s) => s.isCompact);
  const { deallocate, isSaving } = useAllocationMutations();

  const [paginationModel, setPaginationModel] = useState<GridPaginationModel>({
    page: 0,
    pageSize: DEFAULT_PAGE_SIZE
  });
  const [sortModel, setSortModel] = useState<GridSortModel>([
    { field: 'allocatedAt', sort: 'desc' }
  ]);
  const [releasing, setReleasing] = useState<PaymentAllocationDto | null>(null);
  const [releaseReason, setReleaseReason] = useState('');

  const paymentId: string | null = payment?.id ?? null;
  const isTerminal: boolean =
    payment !== null &&
    (payment.status === PaymentStatus.Cancelled || payment.status === PaymentStatus.Reversed);
  const isAllocatable: boolean =
    payment !== null &&
    (payment.status === PaymentStatus.Confirmed || payment.status === PaymentStatus.Posted);

  const filterRequest = useMemo<FilterRequest>(
    () => ({
      page: paginationModel.page + 1,
      pageSize: Math.min(paginationModel.pageSize, MAX_PAGE_SIZE),
      sort: sortModel
        .filter((s) => s.sort)
        .map((s) => ({ field: s.field, direction: s.sort === 'desc' ? 'desc' : 'asc' }))
    }),
    [paginationModel, sortModel]
  );

  const { data, isFetching, error } = useQuery({
    queryKey: ['payment-allocations', paymentId, filterRequest],
    queryFn: () => searchPaymentAllocations(paymentId as string, filterRequest),
    enabled: paymentId !== null,
    placeholderData: (prev) => prev
  });

  useEffect(() => {
    if (error) {
      notification.error(getApiErrorMessage(error, t));
    }
  }, [error, t]);

  useEffect(() => {
    if (payment === null) {
      setReleasing(null);
      setReleaseReason('');
    }
  }, [payment]);

  async function confirmRelease() {
    if (!payment || !releasing) {
      return;
    }
    const result: DeallocatePaymentResultDto | null = await deallocate({
      paymentId: payment.id,
      allocationId: releasing.id,
      rowVersion: rowVersion ?? payment.rowVersion,
      reason: releaseReason.trim() === '' ? undefined : releaseReason.trim()
    });
    if (result) {
      onDeallocated(result);
      setReleasing(null);
      setReleaseReason('');
    }
  }

  const columns = useMemo<GridColDef<PaymentAllocationDto>[]>(
    () => [
      {
        field: 'invoiceDocumentNumber',
        headerName: t('allocations.invoice'),
        width: 160,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        sortable: false,
        renderCell: (params) => (
          // The invoice document number MAY be null while the local projection catches up; the tooltip
          // always carries the invoice id so the row stays correlatable. It anchors to a span because
          // MUI Tooltip needs a ref-holding element.
          <Tooltip title={params.row.invoiceId}>
            <span>
              <CodeText>{params.row.invoiceDocumentNumber ?? '—'}</CodeText>
            </span>
          </Tooltip>
        )
      },
      {
        field: 'invoiceDueDate',
        headerName: t('allocations.invoiceDueDate'),
        width: 130,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        sortable: false,
        renderCell: (params) => <CodeText>{formatDate(params.row.invoiceDueDate)}</CodeText>
      },
      {
        field: 'invoiceGrossTotal',
        headerName: t('allocations.invoiceGrossTotal'),
        width: 140,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) =>
          params.row.invoiceGrossTotal !== null ? (
            <MoneyText amount={params.row.invoiceGrossTotal} />
          ) : (
            <span>—</span>
          )
      },
      {
        field: 'allocatedAmount',
        headerName: t('allocations.allocatedAmount'),
        width: 150,
        ...ledgerMonoColumn,
        sortable: true,
        renderCell: (params) => (
          <MoneyText
            amount={params.row.allocatedAmount}
            currency={payment?.currencyCode ?? undefined}
          />
        )
      },
      {
        field: 'baseAllocatedAmount',
        headerName: t('allocations.baseAllocatedAmount'),
        width: 150,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) => <MoneyText amount={params.row.baseAllocatedAmount} />
      },
      {
        field: 'invoiceStatus',
        headerName: t('allocations.invoiceStatus'),
        width: 120,
        sortable: false,
        renderCell: (params) => params.row.invoiceStatus ?? '—'
      },
      {
        field: 'invoiceSettlementStatus',
        headerName: t('allocations.settlementStatus'),
        width: 150,
        sortable: false,
        renderCell: (params) =>
          params.row.invoiceSettlementStatus !== null
            ? t(settlementStatusLabelKey(params.row.invoiceSettlementStatus))
            : '—'
      },
      {
        field: 'realizedFxDifference',
        headerName: t('allocations.realizedFxDifference'),
        width: 150,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) => (
          <Tooltip title={t('allocations.realizedFxInformational')}>
            <span>
              <MoneyText amount={params.row.realizedFxDifference} />
            </span>
          </Tooltip>
        )
      },
      {
        field: 'allocatedAt',
        headerName: t('allocations.allocatedAt'),
        width: 130,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        sortable: true,
        renderCell: (params) => <CodeText>{formatDate(params.row.allocatedAt)}</CodeText>
      },
      {
        field: 'actions',
        headerName: '',
        width: 64,
        sortable: false,
        align: 'right',
        renderCell: (params) =>
          isTerminal ? null : (
            <AppButton
              variant="text"
              size="small"
              color="error"
              aria-label={t('allocations.deallocate')}
              onClick={() => setReleasing(params.row)}
              sx={{ minWidth: 0, px: 1 }}
            >
              <LinkOffIcon fontSize="small" />
            </AppButton>
          )
      }
    ],
    [t, payment, isTerminal]
  );

  const rows = data?.items ?? [];

  return (
    <>
      <Dialog open={payment !== null} onClose={isSaving ? undefined : onClose} maxWidth="xl" fullWidth>
        <DialogContent sx={{ pt: 3 }}>
          <Typography
            component="h2"
            sx={{ fontFamily: serifFamily, fontWeight: 500, fontSize: '1.375rem', mb: 1 }}
          >
            {t('allocations.title')}
          </Typography>
          <Box sx={{ height: '1px', backgroundColor: 'divider', mb: isCompact ? 2 : 3 }} />

          <Stack spacing={isCompact ? 1.5 : 2} sx={{ mb: isCompact ? 2 : 3 }}>
            <Box sx={{ display: 'flex', gap: 3, flexWrap: 'wrap', alignItems: 'baseline' }}>
              <Box>
                <Typography variant="overline" component="div">
                  {t('payments.documentNumber')}
                </Typography>
                <CodeText>{payment?.documentNumber ?? '—'}</CodeText>
              </Box>
              <Box>
                <Typography variant="overline" component="div">
                  {t('payments.allocatedAmount')}
                </Typography>
                <MoneyText
                  amount={payment?.allocatedAmount ?? 0}
                  currency={payment?.currencyCode ?? undefined}
                />
              </Box>
              <Box>
                <Typography variant="overline" component="div">
                  {t('allocations.remainingUnallocated')}
                </Typography>
                <MoneyText
                  amount={payment?.unallocatedAmount ?? 0}
                  currency={payment?.currencyCode ?? undefined}
                />
              </Box>
              {isAllocatable ? (
                <Box sx={{ ml: 'auto' }}>
                  <AppButton variant="outlined" startIcon={<AddIcon />} onClick={onAllocate}>
                    {t('allocations.allocate')}
                  </AppButton>
                </Box>
              ) : null}
            </Box>

            <Typography variant="caption" sx={{ color: 'text.secondary' }}>
              {t('allocations.realizedFxInformational')}
            </Typography>
            <Typography variant="caption" sx={{ color: 'text.secondary' }}>
              {t('allocations.noAmendHint')}
            </Typography>
          </Stack>

          <DataTable<PaymentAllocationDto>
            rows={rows}
            columns={columns}
            getRowId={(row) => row.id}
            loading={isFetching}
            rowCount={data?.totalCount ?? 0}
            paginationModel={paginationModel}
            onPaginationModelChange={setPaginationModel}
            sortModel={sortModel}
            onSortModelChange={setSortModel}
            emptyTitle={t('allocations.empty')}
            emptyDescription={t('allocations.emptyHint')}
            emptyAction={
              isAllocatable ? (
                <AppButton variant="outlined" startIcon={<AddIcon />} onClick={onAllocate}>
                  {t('allocations.allocate')}
                </AppButton>
              ) : undefined
            }
          />
        </DialogContent>

        <DialogActions sx={{ px: 3, pb: 2 }}>
          <AppButton variant="text" onClick={onClose} disabled={isSaving}>
            {t('common.close')}
          </AppButton>
        </DialogActions>
      </Dialog>

      <Dialog
        open={releasing !== null}
        onClose={isSaving ? undefined : () => setReleasing(null)}
        maxWidth="xs"
        fullWidth
      >
        <DialogContent sx={{ pt: 3 }}>
          <Typography
            component="h2"
            sx={{ fontFamily: serifFamily, fontWeight: 500, fontSize: '1.25rem', mb: 1 }}
          >
            {t('allocations.deallocateTitle')}
          </Typography>
          <Box sx={{ height: '1px', backgroundColor: 'divider', mb: 2 }} />
          <Typography variant="body2" sx={{ color: 'text.secondary', mb: 2 }}>
            {t('allocations.deallocateMessage')}
          </Typography>
          <FormField label={t('allocations.deallocateReasonLabel')}>
            <AppTextField
              multiline
              minRows={2}
              value={releaseReason}
              onChange={(e) => setReleaseReason(e.target.value)}
            />
          </FormField>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <AppButton variant="text" onClick={() => setReleasing(null)} disabled={isSaving}>
            {t('common.cancel')}
          </AppButton>
          <AppButton variant="contained" color="error" onClick={confirmRelease} disabled={isSaving}>
            {t('allocations.deallocate')}
          </AppButton>
        </DialogActions>
      </Dialog>
    </>
  );
}
