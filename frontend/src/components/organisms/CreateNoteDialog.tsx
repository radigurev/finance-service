import { useTranslation } from 'react-i18next';
import { Dialog, DialogContent, Typography, Box } from '@mui/material';
import { serifFamily } from '@/shared/theme';
import { InvoiceFormDialog } from './InvoiceFormDialog';
import { InvoiceDocumentType, type InvoiceDto } from '@/features/invoices/types';

interface CreateNoteDialogProps {
  /** The POSTED invoice being corrected; `null` keeps the dialog closed. */
  original: InvoiceDto | null;
  /** Whether to create a Credit Note (reduces) or a Debit Note (increases). */
  noteType: InvoiceDocumentType.CreditNote | InvoiceDocumentType.DebitNote;
  onClose: () => void;
  /** Called after the note draft is created so the caller can close + refresh. */
  onSaved: () => void;
}

/**
 * Credit/Debit-Note correction dialog (SDD-UI-FIN-001 §2.9). Opens {@link InvoiceFormDialog} in
 * create mode pre-set with the note's {@link InvoiceDocumentType} and `correctsInvoiceId` linking
 * back to the POSTED original, then follows the normal create → confirm → post flow. The original
 * posted invoice's lines, totals, and number are never mutated — the note is a separate document.
 * When there is no original (the dialog is closed) a hairline placeholder dialog renders nothing.
 */
export function CreateNoteDialog({ original, noteType, onClose, onSaved }: CreateNoteDialogProps) {
  const { t } = useTranslation();

  if (!original) {
    return (
      <Dialog open={false} onClose={onClose}>
        <DialogContent>
          <Box sx={{ height: '1px', backgroundColor: 'divider' }} />
          <Typography sx={{ fontFamily: serifFamily }} component="span">
            {t('invoices.noteTitle')}
          </Typography>
        </DialogContent>
      </Dialog>
    );
  }

  return (
    <InvoiceFormDialog
      open={original !== null}
      invoice={null}
      presetDocumentType={noteType}
      correctsInvoiceId={original.id}
      onClose={onClose}
      onSaved={onSaved}
    />
  );
}
