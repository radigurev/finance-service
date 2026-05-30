import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';
import { createCurrency, updateCurrency } from './api';
import type { CurrencyDto, CreateCurrencyRequest, UpdateCurrencyRequest } from './types';

interface UpdateArgs {
  isoCode: string;
  request: UpdateCurrencyRequest;
}

interface UseCurrencyMutations {
  create: (request: CreateCurrencyRequest) => Promise<CurrencyDto | null>;
  update: (args: UpdateArgs) => Promise<CurrencyDto | null>;
  /** Soft-delete: re-issues the update with `isActive = false` (SDD-NOM-001 §2.1). */
  deactivate: (currency: CurrencyDto) => Promise<CurrencyDto | null>;
  isSaving: boolean;
}

/**
 * Create / update / deactivate mutations for currencies. On success the currency list and
 * the shared nomenclature currency cache are invalidated and a success toast is shown; on
 * failure the error is mapped through {@link getApiErrorMessage} and surfaced via
 * {@link notification} — never raw. Returns `null` (rather than throwing) on failure so
 * callers can keep the dialog open. Deactivation is a soft-delete — there is no hard DELETE.
 */
export function useCurrencyMutations(): UseCurrencyMutations {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  async function invalidate(): Promise<void> {
    await queryClient.invalidateQueries({ queryKey: ['currencies'] });
    await queryClient.invalidateQueries({ queryKey: ['nomenclature', 'currencies'] });
  }

  const createMutation = useMutation({
    mutationFn: createCurrency,
    onSuccess: async () => {
      await invalidate();
      notification.success(t('currencies.created'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  const updateMutation = useMutation({
    mutationFn: ({ isoCode, request }: UpdateArgs) => updateCurrency(isoCode, request),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('currencies.updated'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  const deactivateMutation = useMutation({
    mutationFn: (currency: CurrencyDto) =>
      updateCurrency(currency.isoCode, {
        name: currency.name,
        symbol: currency.symbol ?? null,
        isActive: false,
        rowVersion: currency.rowVersion
      }),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('currencies.deactivated'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  return {
    create: (request) => createMutation.mutateAsync(request).catch(() => null),
    update: (args) => updateMutation.mutateAsync(args).catch(() => null),
    deactivate: (currency) => deactivateMutation.mutateAsync(currency).catch(() => null),
    isSaving:
      createMutation.isPending || updateMutation.isPending || deactivateMutation.isPending
  };
}
