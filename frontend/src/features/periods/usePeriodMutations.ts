import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';
import { closePeriod, generatePeriods, reopenPeriod } from './api';
import type { FiscalPeriodDto, GeneratePeriodsRequest } from './types';

interface CloseArgs {
  id: number;
  reason: string;
  rowVersion: string;
}

interface ReopenArgs {
  id: number;
  reason: string;
  rowVersion: string;
}

interface UsePeriodMutations {
  generate: (request: GeneratePeriodsRequest) => Promise<FiscalPeriodDto[] | null>;
  close: (args: CloseArgs) => Promise<FiscalPeriodDto | null>;
  reopen: (args: ReopenArgs) => Promise<FiscalPeriodDto | null>;
  isSaving: boolean;
}

/**
 * Generate / close / reopen mutations for fiscal periods (SDD-FIN-004). On success the
 * periods list cache is invalidated and a success toast is shown; on failure the error is
 * mapped through {@link getApiErrorMessage} and surfaced via {@link notification} — never raw.
 * Mutating operations resolve to `null` (rather than throwing) on failure so callers can keep
 * their dialog open.
 */
export function usePeriodMutations(): UsePeriodMutations {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  function invalidate(): Promise<void> {
    return queryClient.invalidateQueries({ queryKey: ['periods'] });
  }

  const generateMutation = useMutation({
    mutationFn: generatePeriods,
    onSuccess: async () => {
      await invalidate();
      notification.success(t('periods.generated'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  const closeMutation = useMutation({
    mutationFn: ({ id, reason, rowVersion }: CloseArgs) =>
      closePeriod(id, { reason, rowVersion }),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('periods.closed'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  const reopenMutation = useMutation({
    mutationFn: ({ id, reason, rowVersion }: ReopenArgs) =>
      reopenPeriod(id, { reason, rowVersion }),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('periods.reopened'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  return {
    generate: (request) => generateMutation.mutateAsync(request).catch(() => null),
    close: (args) => closeMutation.mutateAsync(args).catch(() => null),
    reopen: (args) => reopenMutation.mutateAsync(args).catch(() => null),
    isSaving: generateMutation.isPending || closeMutation.isPending || reopenMutation.isPending
  };
}
