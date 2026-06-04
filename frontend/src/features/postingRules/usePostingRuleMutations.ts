import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';
import type { JournalEntryDto } from '@/features/journal/types';
import { applyPostingRule, createPostingRule, updatePostingRule } from './api';
import type {
  ApplyPostingRuleRequest,
  CreatePostingRuleRequest,
  PostingRuleDto,
  UpdatePostingRuleRequest
} from './types';

interface UpdateArgs {
  id: number;
  request: UpdatePostingRuleRequest;
}

interface UsePostingRuleMutations {
  create: (request: CreatePostingRuleRequest) => Promise<PostingRuleDto | null>;
  update: (args: UpdateArgs) => Promise<PostingRuleDto | null>;
  apply: (request: ApplyPostingRuleRequest) => Promise<JournalEntryDto | null>;
  isSaving: boolean;
  isApplying: boolean;
}

/**
 * Create / update / apply mutations for posting rules (SDD-FIN-006). On a successful create or
 * update the posting-rules list cache is invalidated (rules are cacheable reference data — the
 * next read sees the change) and a success toast is shown; apply also refreshes the journal-entries
 * list since it produces a (draft or posted) entry. On failure the error is mapped through
 * {@link getApiErrorMessage} and surfaced via {@link notification} — never raw. Mutating operations
 * resolve to `null` (rather than throwing) on failure so callers can keep their dialog open.
 */
export function usePostingRuleMutations(): UsePostingRuleMutations {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  function invalidateRules(): Promise<void> {
    return queryClient.invalidateQueries({ queryKey: ['posting-rules'] });
  }

  const createMutation = useMutation({
    mutationFn: createPostingRule,
    onSuccess: async () => {
      await invalidateRules();
      notification.success(t('postingRules.created'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, request }: UpdateArgs) => updatePostingRule(id, request),
    onSuccess: async () => {
      await invalidateRules();
      notification.success(t('postingRules.updated'));
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  const applyMutation = useMutation({
    mutationFn: applyPostingRule,
    onSuccess: async (entry: JournalEntryDto) => {
      await queryClient.invalidateQueries({ queryKey: ['journal-entries'] });
      notification.success(
        entry.entryNumber ? t('postingRules.appliedPosted') : t('postingRules.appliedDraft')
      );
    },
    onError: (err) => notification.error(getApiErrorMessage(err, t))
  });

  return {
    create: (request) => createMutation.mutateAsync(request).catch(() => null),
    update: (args) => updateMutation.mutateAsync(args).catch(() => null),
    apply: (request) => applyMutation.mutateAsync(request).catch(() => null),
    isSaving: createMutation.isPending || updateMutation.isPending,
    isApplying: applyMutation.isPending
  };
}
