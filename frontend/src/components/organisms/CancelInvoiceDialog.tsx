import { useTranslation } from 'react-i18next';
import { ReasonPromptDialog } from '@/components/molecules';
import { useInvoiceMutations } from '@/features/invoices/useInvoiceMutations';
import type { InvoiceDto } from '@/features/invoices/types';

interface CancelInvoiceDialogProps {
  /** The draft/confirmed invoice to cancel; `null` keeps the dialog closed. */
  invoice: InvoiceDto | null;
  onClose: () => void;
  /** Called after a successful cancellation so the caller can close + refresh. */
  onCancelled: () => void;
}

/**
 * Reason-prompt dialog for cancelling (voiding) a DRAFT or CONFIRMED invoice (SDD-UI-FIN-001 §2.7;
 * SDD-INV-001 §2.6). A non-empty reason is mandatory — the confirm action stays disabled until a
 * reason is entered (the shared {@link ReasonPromptDialog} enforces this). A confirmed invoice
 * keeps its gapless document number after cancellation. Failures surface through the mutation
 * hook's `notification.error(getApiErrorMessage(...))`.
 */
export function CancelInvoiceDialog({ invoice, onClose, onCancelled }: CancelInvoiceDialogProps) {
  const { t } = useTranslation();
  const { cancel, isSaving } = useInvoiceMutations();

  async function handleConfirm(reason: string) {
    if (!invoice) {
      return;
    }
    const result = await cancel({ id: invoice.id, reason, rowVersion: invoice.rowVersion });
    if (result) {
      onCancelled();
    }
  }

  return (
    <ReasonPromptDialog
      open={invoice !== null}
      title={t('invoices.cancelTitle')}
      message={t('invoices.cancelMessage', {
        number: invoice?.documentNumber ?? t('invoices.draftPlaceholder')
      })}
      confirmLabel={t('invoices.cancel')}
      destructive
      busy={isSaving}
      onConfirm={handleConfirm}
      onCancel={onClose}
    />
  );
}
