import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';
import { allocatePayment, deallocatePayment, type DeallocateArgs } from './api';
import type {
  AllocatePaymentItem,
  AllocatePaymentResultDto,
  DeallocatePaymentResultDto
} from './types';

interface AllocateArgs {
  paymentId: string;
  items: AllocatePaymentItem[];
  rowVersion: string;
}

interface DeallocateMutationArgs extends DeallocateArgs {
  paymentId: string;
  allocationId: number;
}

interface UseAllocationMutations {
  allocate: (args: AllocateArgs) => Promise<AllocatePaymentResultDto | null>;
  deallocate: (args: DeallocateMutationArgs) => Promise<DeallocatePaymentResultDto | null>;
  isSaving: boolean;
}

/**
 * Allocate / deallocate mutations (SDD-UI-FIN-002 §2.11, §2.12; SDD-PAY-002). Both writes move the
 * payment's allocation figures AND the settlement state of every invoice they touch, so both
 * invalidate the payments list, the allocations list, the open-items worklist, the aging report, and
 * the counterparty balances (§2.11). Nothing here is cached beyond TanStack Query's short-lived
 * client cache — this is transactional data (§2.16).
 *
 * Allocation is ALL-OR-NOTHING: on failure nothing is optimistically decremented, the dialog stays
 * open, and the mapped error toast is the only change (§2.18). Both calls return the result DTO
 * verbatim so the caller can consume the new `allocatedAmount` / `unallocatedAmount`, the affected
 * invoices' settlement state, and — critically — RE-SEED the payment `rowVersion`, since allocation
 * increments it and a token captured from the list query goes stale immediately (§1.4 trap 11).
 */
export function useAllocationMutations(): UseAllocationMutations {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  async function invalidate(): Promise<void> {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['payments'] }),
      queryClient.invalidateQueries({ queryKey: ['payment-allocations'] }),
      queryClient.invalidateQueries({ queryKey: ['open-items'] }),
      queryClient.invalidateQueries({ queryKey: ['aging'] }),
      queryClient.invalidateQueries({ queryKey: ['counterparty-balances'] })
    ]);
  }

  const allocateMutation = useMutation({
    mutationFn: ({ paymentId, items, rowVersion }: AllocateArgs) =>
      allocatePayment(paymentId, { items, rowVersion }),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('allocations.allocated'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  const deallocateMutation = useMutation({
    mutationFn: ({ paymentId, allocationId, rowVersion, reason }: DeallocateMutationArgs) =>
      deallocatePayment(paymentId, allocationId, { rowVersion, reason }),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('allocations.deallocated'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  return {
    allocate: (args) => allocateMutation.mutateAsync(args).catch(() => null),
    deallocate: (args) => deallocateMutation.mutateAsync(args).catch(() => null),
    isSaving: allocateMutation.isPending || deallocateMutation.isPending
  };
}
