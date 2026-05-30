import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';
import { createAccount, updateAccount } from './api';
import type { AccountDto, CreateAccountRequest, UpdateAccountRequest } from './types';

interface UpdateArgs {
  id: number;
  request: UpdateAccountRequest;
}

interface UseAccountMutations {
  create: (request: CreateAccountRequest) => Promise<AccountDto | null>;
  update: (args: UpdateArgs) => Promise<AccountDto | null>;
  isSaving: boolean;
}

/**
 * Create / update mutations for accounts. On success the accounts list cache is
 * invalidated and a success toast is shown; on failure the error is mapped through
 * {@link getApiErrorMessage} and surfaced via {@link notification} — never raw.
 * Returns `null` (rather than throwing) on failure so callers can keep the dialog open.
 */
export function useAccountMutations(): UseAccountMutations {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  function invalidate(): Promise<void> {
    return queryClient.invalidateQueries({ queryKey: ['accounts'] });
  }

  const createMutation = useMutation({
    mutationFn: createAccount,
    onSuccess: async () => {
      await invalidate();
      notification.success(t('accounts.created'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, request }: UpdateArgs) => updateAccount(id, request),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('accounts.updated'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  return {
    create: (request) => createMutation.mutateAsync(request).catch(() => null),
    update: (args) => updateMutation.mutateAsync(args).catch(() => null),
    isSaving: createMutation.isPending || updateMutation.isPending
  };
}
