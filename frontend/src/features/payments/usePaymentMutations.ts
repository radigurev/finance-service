import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorCode, getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';
import {
  cancelPayment,
  confirmPayment,
  createPayment,
  deletePayment,
  postPayment,
  reversePayment,
  updatePayment
} from './api';
import type { CreatePaymentRequest, PaymentDto, UpdatePaymentRequest } from './types';

interface UpdateArgs {
  id: string;
  request: UpdatePaymentRequest;
}

interface RowVersionArgs {
  id: string;
  rowVersion: string;
}

interface ReasonArgs {
  id: string;
  reason: string;
  rowVersion: string;
}

interface UsePaymentMutations {
  create: (request: CreatePaymentRequest) => Promise<PaymentDto | null>;
  update: (args: UpdateArgs) => Promise<PaymentDto | null>;
  remove: (id: string) => Promise<boolean>;
  confirm: (args: RowVersionArgs) => Promise<PaymentDto | null>;
  post: (args: RowVersionArgs) => Promise<PaymentDto | null>;
  cancel: (args: ReasonArgs) => Promise<PaymentDto | null>;
  reverse: (args: ReasonArgs) => Promise<PaymentDto | null>;
  isSaving: boolean;
}

/**
 * Create / update / delete / confirm / post / cancel / reverse mutations for payments
 * (SDD-UI-FIN-002 §2.3, §2.5–§2.9; SDD-PAY-001). On success the payments cache is invalidated and a
 * success toast is shown; on failure the error is mapped through {@link getApiErrorMessage} and
 * surfaced via {@link notification} — never `err.message`, a raw `detail`, or a status. Mutating
 * operations resolve to `null` / `false` (rather than throwing) on failure so callers keep their
 * dialog open (mirrors the invoices pattern).
 *
 * Three failures get PRESENTATION overrides, because their generic error copy would mislead:
 *
 * - **`PAYMENT_POSTING_PENDING`** is a NORMAL transient state, not a destructive error. It means the
 *   Journal handshake has not landed and this very call RE-ENQUEUED `PaymentConfirmedEvent`. It is
 *   surfaced as an informational "retry queued" toast, the caches are still invalidated so the
 *   transition to `Posted` is observed, and the Post action stays available (§1.4 trap 6, §2.7). It
 *   MUST stay distinguishable from `PAYMENT_NOT_CONFIRMED`, which is a genuine wrong-state post.
 * - **`INVALID_PAYMENT_STATE_TRANSITION` on cancel** points the operator at REVERSAL, because
 *   `Confirmed → Cancelled` was deliberately removed (§2.8).
 * - **`PAYMENT_PERIOD_CLOSED` on reverse** says the period must be REOPENED — the reversing entry
 *   keeps the original entry date, so "try again later" would be wrong copy (§2.9).
 */
export function usePaymentMutations(): UsePaymentMutations {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  function invalidate(): Promise<void> {
    return queryClient.invalidateQueries({ queryKey: ['payments'] });
  }

  const createMutation = useMutation({
    mutationFn: createPayment,
    onSuccess: async () => {
      await invalidate();
      notification.success(t('payments.created'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, request }: UpdateArgs) => updatePayment(id, request),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('payments.updated'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deletePayment(id),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('payments.deleted'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  const confirmMutation = useMutation({
    mutationFn: ({ id, rowVersion }: RowVersionArgs) => confirmPayment(id, { rowVersion }),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('payments.confirmed'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  const postMutation = useMutation({
    mutationFn: ({ id, rowVersion }: RowVersionArgs) => postPayment(id, { rowVersion }),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('payments.posted'));
    },
    onError: async (err) => {
      if (getApiErrorCode(err) === 'PAYMENT_POSTING_PENDING') {
        notification.info(t('payments.postingPendingQueued'));
        await invalidate();
        return;
      }
      notification.error(getApiErrorMessage(err, t));
    }
  });

  const cancelMutation = useMutation({
    mutationFn: ({ id, reason, rowVersion }: ReasonArgs) =>
      cancelPayment(id, { reason, rowVersion }),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('payments.cancelled'));
    },
    onError: (err) => {
      const message: string = getApiErrorMessage(err, t);
      if (getApiErrorCode(err) === 'INVALID_PAYMENT_STATE_TRANSITION') {
        notification.error(`${message} ${t('payments.cancelNotAvailableHint')}`);
        return;
      }
      notification.error(message);
    }
  });

  const reverseMutation = useMutation({
    mutationFn: ({ id, reason, rowVersion }: ReasonArgs) =>
      reversePayment(id, { reason, rowVersion }),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('payments.reversed'));
    },
    onError: (err) => {
      const message: string = getApiErrorMessage(err, t);
      if (getApiErrorCode(err) === 'PAYMENT_PERIOD_CLOSED') {
        notification.error(`${message} ${t('payments.reversePeriodClosedHint')}`);
        return;
      }
      notification.error(message);
    }
  });

  return {
    create: (request) => createMutation.mutateAsync(request).catch(() => null),
    update: (args) => updateMutation.mutateAsync(args).catch(() => null),
    remove: (id) =>
      deleteMutation
        .mutateAsync(id)
        .then(() => true)
        .catch(() => false),
    confirm: (args) => confirmMutation.mutateAsync(args).catch(() => null),
    post: (args) => postMutation.mutateAsync(args).catch(() => null),
    cancel: (args) => cancelMutation.mutateAsync(args).catch(() => null),
    reverse: (args) => reverseMutation.mutateAsync(args).catch(() => null),
    isSaving:
      createMutation.isPending ||
      updateMutation.isPending ||
      deleteMutation.isPending ||
      confirmMutation.isPending ||
      postMutation.isPending ||
      cancelMutation.isPending ||
      reverseMutation.isPending
  };
}
