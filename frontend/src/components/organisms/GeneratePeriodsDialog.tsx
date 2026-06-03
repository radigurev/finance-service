import { useEffect } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Dialog, DialogContent, DialogActions, Typography, Box } from '@mui/material';
import { useTranslation } from 'react-i18next';
import { AppButton, AppTextField } from '@/components/atoms';
import { FormField } from '@/components/molecules';
import { serifFamily } from '@/shared/theme';
import { usePeriodMutations } from '@/features/periods/usePeriodMutations';
import {
  generatePeriodsFormSchema,
  type GeneratePeriodsFormValues
} from '@/features/periods/schema';

interface GeneratePeriodsDialogProps {
  open: boolean;
  onClose: () => void;
  /** Called after a successful generation so the caller can close + refresh. */
  onGenerated: () => void;
}

/**
 * Generate-year dialog: prompts for a fiscal year and generates the 12 calendar-aligned monthly
 * periods server-side (SDD-FIN-004 §2.2). Uses react-hook-form + zod; failures (e.g. an
 * overlapping or duplicate period) surface through the mutation hook's
 * `notification.error(getApiErrorMessage(...))`.
 */
export function GeneratePeriodsDialog({ open, onClose, onGenerated }: GeneratePeriodsDialogProps) {
  const { t } = useTranslation();
  const { generate, isSaving } = usePeriodMutations();

  const {
    control,
    handleSubmit,
    reset,
    formState: { errors }
  } = useForm<GeneratePeriodsFormValues>({
    resolver: zodResolver(generatePeriodsFormSchema),
    defaultValues: { fiscalYear: new Date().getFullYear() }
  });

  useEffect(() => {
    if (open) {
      reset({ fiscalYear: new Date().getFullYear() });
    }
  }, [open, reset]);

  async function onSubmit(values: GeneratePeriodsFormValues) {
    const result = await generate({ fiscalYear: values.fiscalYear });
    if (result) {
      onGenerated();
    }
  }

  return (
    <Dialog open={open} onClose={isSaving ? undefined : onClose} maxWidth="xs" fullWidth>
      <DialogContent sx={{ pt: 3 }}>
        <Typography
          component="h2"
          sx={{ fontFamily: serifFamily, fontWeight: 500, fontSize: '1.25rem', mb: 1 }}
        >
          {t('periods.generateTitle')}
        </Typography>
        <Box sx={{ height: '1px', backgroundColor: 'divider', mb: 2 }} />
        <Typography variant="body2" sx={{ color: 'text.secondary', mb: 2 }}>
          {t('periods.generateMessage')}
        </Typography>
        <form id="generate-periods-form" onSubmit={handleSubmit(onSubmit)} noValidate>
          <Controller
            name="fiscalYear"
            control={control}
            render={({ field }) => (
              <FormField
                label={t('periods.fiscalYear')}
                required
                error={errors.fiscalYear?.message ? t(errors.fiscalYear.message) : undefined}
              >
                <AppTextField
                  type="number"
                  value={field.value}
                  autoFocus
                  error={Boolean(errors.fiscalYear)}
                  onChange={(e) => field.onChange(Number(e.target.value))}
                  inputProps={{ min: 2000, max: 2100, step: 1 }}
                />
              </FormField>
            )}
          />
        </form>
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2 }}>
        <AppButton variant="text" onClick={onClose} disabled={isSaving}>
          {t('common.cancel')}
        </AppButton>
        <AppButton
          type="submit"
          form="generate-periods-form"
          variant="contained"
          disabled={isSaving}
        >
          {isSaving ? t('periods.generating') : t('periods.generate')}
        </AppButton>
      </DialogActions>
    </Dialog>
  );
}
