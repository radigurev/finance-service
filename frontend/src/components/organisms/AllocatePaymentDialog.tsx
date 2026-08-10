import { useEffect, useMemo, useState } from 'react';
import {
  Box,
  Checkbox,
  Dialog,
  DialogActions,
  DialogContent,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography
} from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { AppButton, AppTextField, CodeText, MoneyText } from '@/components/atoms';
import { EmptyState } from '@/components/molecules';
import { useLayoutStore } from '@/shared/stores/layout';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';
import { MAX_PAGE_SIZE } from '@/shared/api/paging';
import { serifFamily } from '@/shared/theme';
import { searchOpenItems, searchPaymentAllocations } from '@/features/payments/api';
import { useAllocationMutations } from '@/features/payments/useAllocationMutations';
import {
  allocateFormSchema,
  roundMoney,
  toAllocationItems,
  type AllocationDraftItem
} from '@/features/payments/schema';
import {
  agingDirectionOf,
  type AllocatePaymentResultDto,
  type OpenItemDto,
  type PaymentDto
} from '@/features/payments/types';

interface AllocatePaymentDialogProps {
  /** The `Confirmed`/`Posted` payment to allocate; `null` keeps the dialog closed. */
  payment: PaymentDto | null;
  /** The freshest `rowVersion` for the payment (re-seeded after any allocate/deallocate). */
  rowVersion?: string;
  onClose: () => void;
  /** Hands back the result DTO so the caller can re-seed `rowVersion` and the figures. */
  onAllocated: (result: AllocatePaymentResultDto) => void;
}

/** One picker row's editable state. */
interface PickerRow {
  selected: boolean;
  amount: number;
}

/** Formats an ISO 8601 timestamp as a `yyyy-MM-dd` calendar date for display. */
function formatDate(value: string): string {
  return value.slice(0, 10);
}

/**
 * The open-item picker + explicit per-invoice amount entry that composes an allocate request
 * (SDD-UI-FIN-002 §2.11; SDD-PAY-002 §2.4, §2.5).
 *
 * Shipped-contract details that shape this dialog:
 *
 * - **Candidates are PRE-NARROWED** to the payment's own `counterpartyId`, `currencyCode`, and
 *   `direction` — the latter converted from the numeric `PaymentDto.direction` into the STRING form
 *   (`"AR"`/`"AP"`) the query contract requires. Pre-narrowing is what turns four of the ten invariant
 *   codes (`_DIRECTION_MISMATCH`, `_COUNTERPARTY_MISMATCH`, `_CURRENCY_MISMATCH`,
 *   `_INVOICE_NOT_ELIGIBLE`) from user-facing errors into unreachable defenses.
 * - **Invoices already allocated to THIS payment are excluded client-side.** `GET /open-items` has no
 *   "exclude allocated by payment X" narrowing and a partially matched invoice keeps a positive
 *   `outstanding`, so the payment's own allocations list is cross-referenced — at the cost of a second
 *   request — to keep `PAYMENT_ALLOCATION_DUPLICATE` off the routine path (§1.6 gap 7).
 * - **Each amount defaults to `min(outstanding, unallocatedAmount)`** and a running total is shown
 *   against the unallocated amount so an over-allocation is visible BEFORE submitting.
 * - **Client pre-checks mirror server invariants 7, 8, and 9 with EXACT two-decimal comparison** — no
 *   epsilon band, because the server compares `DECIMAL(18,2)` values with no tolerance. Submit stays
 *   disabled while any check fails (§2.11, §2.18).
 * - **The call is ALL-OR-NOTHING.** On failure nothing is optimistically decremented, the dialog stays
 *   open, and the mapped error toast is the only change.
 * - **On success the `AllocatePaymentResultDto` is consumed directly** — it carries the new figures,
 *   the affected invoices' settlement state, and the new `rowVersion`, so NO follow-up read is issued
 *   (§1.4 traps 10/11).
 *
 * There is deliberately NO "apply to oldest first" / FIFO button: `Items` is required and an empty list
 * is rejected with `PAYMENT_ALLOCATION_ITEMS_REQUIRED` — it is never read as "apply the whole payment"
 * (§1.6 gap 5).
 */
export function AllocatePaymentDialog({
  payment,
  rowVersion,
  onClose,
  onAllocated
}: AllocatePaymentDialogProps) {
  const { t } = useTranslation();
  const isCompact = useLayoutStore((s) => s.isCompact);
  const { allocate, isSaving } = useAllocationMutations();

  const [rowState, setRowState] = useState<Record<string, PickerRow>>({});

  const paymentId: string | null = payment?.id ?? null;

  const openItemsQuery = useQuery({
    queryKey: ['open-items', 'allocate-picker', paymentId],
    queryFn: () =>
      searchOpenItems(
        {
          counterpartyId: payment?.counterpartyId,
          currencyCode: payment?.currencyCode,
          direction: payment ? agingDirectionOf(payment.direction) : undefined
        },
        {
          page: 1,
          pageSize: MAX_PAGE_SIZE,
          sort: [{ field: 'dueDate', direction: 'asc' }]
        }
      ),
    enabled: paymentId !== null,
    staleTime: 0
  });

  const allocationsQuery = useQuery({
    queryKey: ['payment-allocations', 'allocate-picker', paymentId],
    queryFn: () =>
      searchPaymentAllocations(paymentId as string, { page: 1, pageSize: MAX_PAGE_SIZE }),
    enabled: paymentId !== null,
    staleTime: 0
  });

  useEffect(() => {
    if (openItemsQuery.error) {
      notification.error(getApiErrorMessage(openItemsQuery.error, t));
    }
  }, [openItemsQuery.error, t]);

  useEffect(() => {
    if (allocationsQuery.error) {
      notification.error(getApiErrorMessage(allocationsQuery.error, t));
    }
  }, [allocationsQuery.error, t]);

  const alreadyAllocated: Set<string> = useMemo(
    () => new Set((allocationsQuery.data?.items ?? []).map((row) => row.invoiceId)),
    [allocationsQuery.data]
  );

  const candidates: OpenItemDto[] = useMemo(
    () => (openItemsQuery.data?.items ?? []).filter((item) => !alreadyAllocated.has(item.invoiceId)),
    [openItemsQuery.data, alreadyAllocated]
  );

  const unallocated: number = payment?.unallocatedAmount ?? 0;

  useEffect(() => {
    if (payment === null) {
      setRowState({});
    }
  }, [payment]);

  function defaultAmount(item: OpenItemDto): number {
    return roundMoney(Math.min(item.outstanding, unallocated));
  }

  function rowFor(item: OpenItemDto): PickerRow {
    return rowState[item.invoiceId] ?? { selected: false, amount: defaultAmount(item) };
  }

  function toggle(item: OpenItemDto, selected: boolean) {
    setRowState((prev) => ({
      ...prev,
      [item.invoiceId]: { selected, amount: prev[item.invoiceId]?.amount ?? defaultAmount(item) }
    }));
  }

  function setAmount(item: OpenItemDto, amount: number) {
    setRowState((prev) => ({
      ...prev,
      [item.invoiceId]: { selected: prev[item.invoiceId]?.selected ?? true, amount }
    }));
  }

  function applyMax(item: OpenItemDto) {
    const remainingBeforeThis: number = roundMoney(
      unallocated -
        candidates
          .filter((other) => other.invoiceId !== item.invoiceId && rowFor(other).selected)
          .reduce((sum, other) => sum + (Number(rowFor(other).amount) || 0), 0)
    );
    setRowState((prev) => ({
      ...prev,
      [item.invoiceId]: {
        selected: true,
        amount: roundMoney(Math.max(0, Math.min(item.outstanding, remainingBeforeThis)))
      }
    }));
  }

  const draftItems: AllocationDraftItem[] = useMemo(
    () =>
      candidates
        .filter((item) => rowFor(item).selected)
        .map((item) => ({ invoiceId: item.invoiceId, allocatedAmount: rowFor(item).amount })),
    // `rowFor` reads `rowState` and falls back to a default derived from `unallocated`, so both are
    // listed even though neither appears literally in the body above.
    [candidates, rowState, unallocated]
  );

  const runningTotal: number = useMemo(
    () => roundMoney(draftItems.reduce((sum, item) => sum + (Number(item.allocatedAmount) || 0), 0)),
    [draftItems]
  );

  const outstandingByInvoice: Record<string, number> = useMemo(
    () =>
      candidates.reduce<Record<string, number>>((acc, item) => {
        acc[item.invoiceId] = item.outstanding;
        return acc;
      }, {}),
    [candidates]
  );

  /**
   * The zod pre-check result. Its first message key is surfaced beneath the running total so the
   * operator sees WHICH bound is breached before submitting; the server remains the authority.
   */
  const validation = useMemo(() => {
    const schema = allocateFormSchema({ unallocatedAmount: unallocated, outstandingByInvoice });
    return schema.safeParse({ items: draftItems });
  }, [draftItems, unallocated, outstandingByInvoice]);

  const validationMessage: string | undefined = validation.success
    ? undefined
    : validation.error.issues[0]?.message;

  async function submit() {
    if (!payment || !validation.success) {
      return;
    }
    const result: AllocatePaymentResultDto | null = await allocate({
      paymentId: payment.id,
      items: toAllocationItems(draftItems),
      rowVersion: rowVersion ?? payment.rowVersion
    });
    if (result) {
      onAllocated(result);
      setRowState({});
    }
  }

  const isLoading: boolean = openItemsQuery.isFetching || allocationsQuery.isFetching;

  return (
    <Dialog
      open={payment !== null}
      onClose={isSaving ? undefined : onClose}
      maxWidth="lg"
      fullWidth
    >
      <DialogContent sx={{ pt: 3 }}>
        <Typography
          component="h2"
          sx={{ fontFamily: serifFamily, fontWeight: 500, fontSize: '1.375rem', mb: 1 }}
        >
          {t('allocations.pickerTitle')}
        </Typography>
        <Box sx={{ height: '1px', backgroundColor: 'divider', mb: isCompact ? 2 : 3 }} />

        <Stack spacing={isCompact ? 1.5 : 2}>
          <Typography variant="caption" sx={{ color: 'text.secondary' }}>
            {t('allocations.pickerHint')}
          </Typography>

          <Box sx={{ display: 'flex', gap: 3, flexWrap: 'wrap', alignItems: 'baseline' }}>
            <Box>
              <Typography variant="overline" component="div">
                {t('allocations.remainingUnallocated')}
              </Typography>
              <MoneyText amount={unallocated} currency={payment?.currencyCode ?? undefined} />
            </Box>
            <Box>
              <Typography variant="overline" component="div">
                {t('allocations.runningTotal')}
              </Typography>
              <MoneyText amount={runningTotal} currency={payment?.currencyCode ?? undefined} />
            </Box>
          </Box>

          {candidates.length === 0 && !isLoading ? (
            <EmptyState title={t('openItems.empty')} description={t('openItems.emptyHint')} />
          ) : (
            <Table size={isCompact ? 'small' : 'medium'}>
              <TableHead>
                <TableRow>
                  <TableCell padding="checkbox" />
                  <TableCell>{t('openItems.documentNumber')}</TableCell>
                  <TableCell>{t('openItems.dueDate')}</TableCell>
                  <TableCell align="right">{t('openItems.outstanding')}</TableCell>
                  <TableCell align="right">{t('allocations.allocatedAmount')}</TableCell>
                  <TableCell />
                </TableRow>
              </TableHead>
              <TableBody>
                {candidates.map((item) => {
                  const row: PickerRow = rowFor(item);
                  return (
                    <TableRow key={item.invoiceId} hover>
                      <TableCell padding="checkbox">
                        <Checkbox
                          checked={row.selected}
                          inputProps={{
                            'aria-label': `${t('allocations.invoice')} ${item.documentNumber}`
                          }}
                          onChange={(e) => toggle(item, e.target.checked)}
                        />
                      </TableCell>
                      <TableCell>
                        <CodeText>{item.documentNumber}</CodeText>
                      </TableCell>
                      <TableCell>
                        <CodeText>{formatDate(item.dueDate)}</CodeText>
                      </TableCell>
                      <TableCell align="right">
                        <MoneyText amount={item.outstanding} />
                      </TableCell>
                      <TableCell align="right" sx={{ width: 170 }}>
                        <AppTextField
                          type="number"
                          value={row.amount}
                          disabled={!row.selected}
                          onChange={(e) => setAmount(item, Number(e.target.value))}
                          inputProps={{
                            min: 0,
                            step: '0.01',
                            'aria-label': `${t('allocations.allocatedAmount')} ${item.documentNumber}`
                          }}
                        />
                      </TableCell>
                      <TableCell align="right">
                        <AppButton variant="text" size="small" onClick={() => applyMax(item)}>
                          {t('allocations.applyMax')}
                        </AppButton>
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          )}

          {validationMessage && draftItems.length > 0 ? (
            <Typography variant="caption" sx={{ color: 'error.main' }}>
              {t(validationMessage)}
            </Typography>
          ) : null}
        </Stack>
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2 }}>
        <AppButton variant="text" onClick={onClose} disabled={isSaving}>
          {t('common.cancel')}
        </AppButton>
        <AppButton
          variant="contained"
          onClick={submit}
          disabled={isSaving || !validation.success}
        >
          {t('allocations.allocate')}
        </AppButton>
      </DialogActions>
    </Dialog>
  );
}
