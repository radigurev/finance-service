import { useTranslation } from 'react-i18next';
import { ReasonPromptDialog } from '@/components/molecules';
import { useJournalMutations } from '@/features/journal/useJournalMutations';
import type { JournalEntryDto } from '@/features/journal/types';

interface ReverseJournalEntryDialogProps {
  /** The posted entry to reverse; `null` keeps the dialog closed. */
  entry: JournalEntryDto | null;
  onClose: () => void;
  /** Called after a successful reversal so the caller can close + refresh. */
  onReversed: () => void;
}

/**
 * Reason-prompt dialog for reversing a POSTED journal entry (SDD-FIN-002 §2.6). A non-empty
 * reason is mandatory (SDD-AUDIT-001). On success the new sign-flipped reversal entry is created
 * server-side; failures surface through the mutation hook's
 * `notification.error(getApiErrorMessage(...))`.
 */
export function ReverseJournalEntryDialog({
  entry,
  onClose,
  onReversed
}: ReverseJournalEntryDialogProps) {
  const { t } = useTranslation();
  const { reverse, isSaving } = useJournalMutations();

  async function handleConfirm(reason: string) {
    if (!entry) {
      return;
    }
    const result = await reverse({ id: entry.id, reason, rowVersion: entry.rowVersion });
    if (result) {
      onReversed();
    }
  }

  return (
    <ReasonPromptDialog
      open={entry !== null}
      title={t('journal.reverseTitle')}
      message={t('journal.reverseMessage', { number: entry?.entryNumber ?? '' })}
      confirmLabel={t('journal.reverse')}
      destructive
      busy={isSaving}
      onConfirm={handleConfirm}
      onCancel={onClose}
    />
  );
}
