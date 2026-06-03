import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';
import {
  createJournalEntry,
  deleteJournalEntry,
  postJournalEntry,
  reverseJournalEntry,
  updateJournalEntry
} from './api';
import type {
  CreateJournalEntryRequest,
  JournalEntryDto,
  UpdateJournalEntryRequest
} from './types';

interface UpdateArgs {
  id: string;
  request: UpdateJournalEntryRequest;
}

interface PostArgs {
  id: string;
  rowVersion: string;
}

interface ReverseArgs {
  id: string;
  reason: string;
  rowVersion: string;
}

interface UseJournalMutations {
  create: (request: CreateJournalEntryRequest) => Promise<JournalEntryDto | null>;
  update: (args: UpdateArgs) => Promise<JournalEntryDto | null>;
  remove: (id: string) => Promise<boolean>;
  post: (args: PostArgs) => Promise<JournalEntryDto | null>;
  reverse: (args: ReverseArgs) => Promise<JournalEntryDto | null>;
  isSaving: boolean;
}

/**
 * Create / update / delete / post / reverse mutations for journal entries (SDD-FIN-002). On
 * success the entries list cache is invalidated and a success toast is shown; on failure the
 * error is mapped through {@link getApiErrorMessage} and surfaced via {@link notification} —
 * never raw. Mutating operations resolve to `null` / `false` (rather than throwing) on failure
 * so callers can keep their dialog open.
 */
export function useJournalMutations(): UseJournalMutations {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  function invalidate(): Promise<void> {
    return queryClient.invalidateQueries({ queryKey: ['journal-entries'] });
  }

  const createMutation = useMutation({
    mutationFn: createJournalEntry,
    onSuccess: async () => {
      await invalidate();
      notification.success(t('journal.created'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, request }: UpdateArgs) => updateJournalEntry(id, request),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('journal.updated'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteJournalEntry(id),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('journal.deleted'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  const postMutation = useMutation({
    mutationFn: ({ id, rowVersion }: PostArgs) => postJournalEntry(id, { rowVersion }),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('journal.posted'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  const reverseMutation = useMutation({
    mutationFn: ({ id, reason, rowVersion }: ReverseArgs) =>
      reverseJournalEntry(id, { reason, rowVersion }),
    onSuccess: async () => {
      await invalidate();
      notification.success(t('journal.reversed'));
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
    post: (args) => postMutation.mutateAsync(args).catch(() => null),
    reverse: (args) => reverseMutation.mutateAsync(args).catch(() => null),
    isSaving:
      createMutation.isPending ||
      updateMutation.isPending ||
      deleteMutation.isPending ||
      postMutation.isPending ||
      reverseMutation.isPending
  };
}
