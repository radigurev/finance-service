import { useTranslation } from 'react-i18next';
import { ReasonPromptDialog } from '@/components/molecules';
import { usePaymentMutations } from '@/features/payments/usePaymentMutations';
import type { PaymentDto } from '@/features/payments/types';

interface CancelPaymentDialogProps {
  /** The DRAFT payment to cancel; `null` keeps the dialog closed. */
  payment: PaymentDto | null;
  /** The freshest `rowVersion` for the payment (re-seeded after any allocate/deallocate). */
  rowVersion?: string;
  onClose: () => void;
  /** Called after a successful cancellation so the caller can close + refresh. */
  onCancelled: () => void;
}

/**
 * Reason-prompt dialog for cancelling a **DRAFT** payment (SDD-UI-FIN-002 §2.8; SDD-PAY-001 §2.6).
 *
 * Cancel is `Draft`-ONLY. `PaymentStatus.Confirmed`'s `AllowedNextStates` is `{ Posted }`, so
 * `Confirmed → Cancelled` was deliberately removed: a confirmed payment is completed to `Posted` and
 * then REVERSED. The caller must never render this dialog for a `Confirmed`/`Posted`/`Cancelled`/
 * `Reversed` payment — this is the single biggest behavioral divergence from the invoices feature,
 * where Cancel IS offered on a `Confirmed` invoice (§1.4 trap 3).
 *
 * Because a draft never held a document number, the message names the payment by its counterparty and
 * amount rather than a number, and a `Cancelled` payment continues to render `—` for its number
 * FOREVER (§1.4 trap 5). A non-empty reason is mandatory — the shared {@link ReasonPromptDialog}
 * keeps submit behind that validation, and `PAYMENT_CANCEL_REASON_REQUIRED` is mapped defensively.
 * Failures surface through the mutation hook's `notification.error(getApiErrorMessage(...))`, with
 * `INVALID_PAYMENT_STATE_TRANSITION` additionally pointing the operator at reversal.
 */
export function CancelPaymentDialog({
  payment,
  rowVersion,
  onClose,
  onCancelled
}: CancelPaymentDialogProps) {
  const { t } = useTranslation();
  const { cancel, isSaving } = usePaymentMutations();

  async function handleConfirm(reason: string) {
    if (!payment) {
      return;
    }
    const result = await cancel({
      id: payment.id,
      reason,
      rowVersion: rowVersion ?? payment.rowVersion
    });
    if (result) {
      onCancelled();
    }
  }

  return (
    <ReasonPromptDialog
      open={payment !== null}
      title={t('payments.cancelTitle')}
      message={t('payments.cancelMessage')}
      confirmLabel={t('payments.cancel')}
      destructive
      busy={isSaving}
      onConfirm={handleConfirm}
      onCancel={onClose}
    />
  );
}
