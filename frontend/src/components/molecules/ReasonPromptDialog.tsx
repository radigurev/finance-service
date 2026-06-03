import { useEffect } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Dialog, DialogContent, DialogActions, Typography, Box } from '@mui/material';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { AppButton, AppTextField } from '@/components/atoms';
import { FormField } from './FormField';
import { serifFamily } from '@/shared/theme';

/** Standalone reason-prompt schema (mandatory non-empty reason). */
const reasonSchema = z.object({
  reason: z
    .string()
    .trim()
    .min(1, 'common.reasonRequired')
    .max(500, 'common.reasonTooLong')
});

type ReasonValues = z.infer<typeof reasonSchema>;

interface ReasonPromptDialogProps {
  open: boolean;
  /** Serif title (already translated). */
  title: string;
  /** Body message above the field (already translated). */
  message: string;
  /** Confirm button label; defaults to the shared `common.confirm` key. */
  confirmLabel?: string;
  /** Renders the confirm action in oxblood for destructive operations. */
  destructive?: boolean;
  /** True while the confirm action is in flight. */
  busy?: boolean;
  /** Receives the trimmed, validated reason on confirm. */
  onConfirm: (reason: string) => void;
  onCancel: () => void;
}

/**
 * Hairline-framed dialog that collects a mandatory reason before a sensitive operation
 * (reverse, close, reopen — SDD-AUDIT-001 mandatory-reason list). Uses react-hook-form + zod;
 * the confirm action stays disabled while busy and forwards the trimmed reason to the caller.
 */
export function ReasonPromptDialog({
  open,
  title,
  message,
  confirmLabel,
  destructive = false,
  busy = false,
  onConfirm,
  onCancel
}: ReasonPromptDialogProps) {
  const { t } = useTranslation();

  const {
    control,
    handleSubmit,
    reset,
    formState: { errors }
  } = useForm<ReasonValues>({
    resolver: zodResolver(reasonSchema),
    defaultValues: { reason: '' }
  });

  useEffect(() => {
    if (open) {
      reset({ reason: '' });
    }
  }, [open, reset]);

  function submit(values: ReasonValues) {
    onConfirm(values.reason.trim());
  }

  return (
    <Dialog open={open} onClose={busy ? undefined : onCancel} maxWidth="xs" fullWidth>
      <DialogContent sx={{ pt: 3 }}>
        <Typography
          component="h2"
          sx={{ fontFamily: serifFamily, fontWeight: 500, fontSize: '1.25rem', mb: 1 }}
        >
          {title}
        </Typography>
        <Box sx={{ height: '1px', backgroundColor: 'divider', mb: 2 }} />
        <Typography variant="body2" sx={{ color: 'text.secondary', mb: 2 }}>
          {message}
        </Typography>
        <form id="reason-form" onSubmit={handleSubmit(submit)} noValidate>
          <Controller
            name="reason"
            control={control}
            render={({ field }) => (
              <FormField
                label={t('common.reason')}
                required
                error={errors.reason?.message ? t(errors.reason.message) : undefined}
              >
                <AppTextField
                  {...field}
                  multiline
                  minRows={2}
                  autoFocus
                  error={Boolean(errors.reason)}
                />
              </FormField>
            )}
          />
        </form>
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2 }}>
        <AppButton variant="text" onClick={onCancel} disabled={busy}>
          {t('common.cancel')}
        </AppButton>
        <AppButton
          type="submit"
          form="reason-form"
          variant="contained"
          color={destructive ? 'error' : 'primary'}
          disabled={busy}
        >
          {confirmLabel ?? t('common.confirm')}
        </AppButton>
      </DialogActions>
    </Dialog>
  );
}
