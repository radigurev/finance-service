import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';
import {
  cancelInvoice,
  confirmInvoice,
  createInvoice,
  deleteInvoice,
  postInvoice,
  updateInvoice
} from './api';
import type { CreateInvoiceRequest, InvoiceDto, UpdateInvoiceRequest } from './types';

interface UpdateArgs {
  id: string;
  request: UpdateInvoiceRequest;
}

interface RowVersionArgs {
  id: string;
  rowVersion: string;
}

interface CancelArgs {
  id: string;
  reason: string;
  rowVersion: string;
}

interface UseInvoiceMutations {
  create: (request: CreateInvoiceRequest) => Promise<InvoiceDto | null>;
  update: (args: UpdateArgs) => Promise<InvoiceDto | null>;
  remove: (id: string) => Promise<boolean>;
  confirm: (args: RowVersionArgs) => Promise<InvoiceDto | null>;
  post: (args: RowVersionArgs) => Promise<InvoiceDto | null>;
  cancel: (args: CancelArgs) => Promise<InvoiceDto | null>;
  isSaving: boolean;
}

/**
 * Create / update / delete / confirm / post / cancel mutations for invoices (SDD-UI-FIN-001 §2;
 * SDD-INV-001). On success the invoices list cache is invalidated and a success toast is shown;
 * on failure the error is mapped through {@link getApiErrorMessage} and surfaced via
 * {@link notification} — never raw. Mutating operations resolve to `null` / `false` (rather than
 * throwing) on failure so callers can keep their dialog open (mirrors the journal pattern).
 */
export function useInvoiceMutations(): UseInvoiceMutations {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  function invalidate(): Promise<void> {
    return queryClient.invalidateQueries({ queryKey: ['invoices'] });
  }

  const createMutation = useMutation({
    mutationFn: createInvoice,
    onSuccess: async () => {
      await invalidate();
      notification.success(t('invoices.created'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, request }: UpdateArgs) => updateInvoice(id, request),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('invoices.updated'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteInvoice(id),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('invoices.deleted'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  const confirmMutation = useMutation({
    mutationFn: ({ id, rowVersion }: RowVersionArgs) => confirmInvoice(id, { rowVersion }),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('invoices.confirmed'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  const postMutation = useMutation({
    mutationFn: ({ id, rowVersion }: RowVersionArgs) => postInvoice(id, { rowVersion }),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('invoices.posted'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  const cancelMutation = useMutation({
    mutationFn: ({ id, reason, rowVersion }: CancelArgs) =>
      cancelInvoice(id, { reason, rowVersion }),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('invoices.cancelled'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
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
    isSaving:
      createMutation.isPending ||
      updateMutation.isPending ||
      deleteMutation.isPending ||
      confirmMutation.isPending ||
      postMutation.isPending ||
      cancelMutation.isPending
  };
}
