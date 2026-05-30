import { Dialog, DialogContent, DialogActions, Typography, Box } from '@mui/material';
import { useTranslation } from 'react-i18next';
import { AppButton } from '@/components/atoms';
import { serifFamily } from '@/shared/theme';

interface ConfirmDialogProps {
  open: boolean;
  /** Serif title (already translated). */
  title: string;
  /** Body message (already translated). */
  message: string;
  /** Confirm button label; defaults to the shared `common.confirm` key. */
  confirmLabel?: string;
  /** Renders the confirm action in oxblood for destructive operations. */
  destructive?: boolean;
  /** True while the confirm action is in flight. */
  busy?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

/**
 * Hairline-framed confirmation dialog. No elevation; serif title; quiet text cancel +
 * a single solid confirm (oxblood when destructive).
 */
export function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel,
  destructive = false,
  busy = false,
  onConfirm,
  onCancel
}: ConfirmDialogProps) {
  const { t } = useTranslation();

  return (
    <Dialog open={open} onClose={onCancel} maxWidth="xs" fullWidth>
      <DialogContent sx={{ pt: 3 }}>
        <Typography
          component="h2"
          sx={{ fontFamily: serifFamily, fontWeight: 500, fontSize: '1.25rem', mb: 1 }}
        >
          {title}
        </Typography>
        <Box sx={{ height: '1px', backgroundColor: 'divider', mb: 2 }} />
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
          {message}
        </Typography>
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2 }}>
        <AppButton variant="text" onClick={onCancel} disabled={busy}>
          {t('common.cancel')}
        </AppButton>
        <AppButton
          variant="contained"
          color={destructive ? 'error' : 'primary'}
          onClick={onConfirm}
          disabled={busy}
        >
          {confirmLabel ?? t('common.confirm')}
        </AppButton>
      </DialogActions>
    </Dialog>
  );
}
