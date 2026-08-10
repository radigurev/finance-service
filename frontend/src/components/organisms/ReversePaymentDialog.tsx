import { useTranslation } from 'react-i18next';
import { ReasonPromptDialog } from '@/components/molecules';
import { usePaymentMutations } from '@/features/payments/usePaymentMutations';
import { displayDocumentNumber, type PaymentDto } from '@/features/payments/types';

interface ReversePaymentDialogProps {
  /** The POSTED payment to reverse; `null` keeps the dialog closed. */
  payment: PaymentDto | null;
  /** The freshest `rowVersion` for the payment (re-seeded after any allocate/deallocate). */
  rowVersion?: string;
  onClose: () => void;
  /** Called after a successful reversal so the caller can close + refresh. */
  onReversed: () => void;
}

/**
 * Reason-prompt dialog for reversing a **POSTED** payment (SDD-UI-FIN-002 §2.9; SDD-PAY-001 §2.7).
 *
 * Reverse is `Posted`-only and is blocked while `allocatedAmount > 0` — allocations are NEVER
 * auto-released, so the caller disables the action with an explanatory tooltip and the server's
 * `PAYMENT_HAS_ALLOCATIONS` (409) stays mapped as a defensive path (§1.4 trap 12).
 *
 * The copy states what reversal actually does: a sign-flipped journal entry is produced and NOTHING on
 * the payment header, amount, or document number changes — the payment is flagged `Reversed` and keeps
 * its number. Reversal is neither an edit nor a deletion. Because the reversing entry keeps the
 * ORIGINAL entry date, `PAYMENT_PERIOD_CLOSED` here means the period must be REOPENED, which the
 * mutation hook appends to the toast rather than saying "try again later". A non-empty reason is
 * mandatory; `PAYMENT_REVERSE_REASON_REQUIRED` is mapped defensively.
 */
export function ReversePaymentDialog({
  payment,
  rowVersion,
  onClose,
  onReversed
}: ReversePaymentDialogProps) {
  const { t } = useTranslation();
  const { reverse, isSaving } = usePaymentMutations();

  async function handleConfirm(reason: string) {
    if (!payment) {
      return;
    }
    const result = await reverse({
      id: payment.id,
      reason,
      rowVersion: rowVersion ?? payment.rowVersion
    });
    if (result) {
      onReversed();
    }
  }

  return (
    <ReasonPromptDialog
      open={payment !== null}
      title={t('payments.reverseTitle')}
      message={t('payments.reverseMessage', {
        number: payment ? displayDocumentNumber(payment) : ''
      })}
      confirmLabel={t('payments.reverse')}
      destructive
      busy={isSaving}
      onConfirm={handleConfirm}
      onCancel={onClose}
    />
  );
}
