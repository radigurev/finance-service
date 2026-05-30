import { useEffect } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import {
  Dialog,
  DialogContent,
  DialogActions,
  Stack,
  Box,
  Typography,
  FormControlLabel,
  Switch
} from '@mui/material';
import { useTranslation } from 'react-i18next';
import { AppButton, AppTextField } from '@/components/atoms';
import { FormField } from '@/components/molecules';
import { serifFamily } from '@/shared/theme';
import { useCurrencyMutations } from '@/features/currencies/useCurrencyMutations';
import { currencyFormSchema, type CurrencyFormValues } from '@/features/currencies/schema';
import type { CurrencyDto } from '@/features/currencies/types';

interface CurrencyFormDialogProps {
  open: boolean;
  /** The currency being edited; `null` opens the create flow. */
  currency: CurrencyDto | null;
  onClose: () => void;
  /** Called after a successful create/update so the caller can close + refresh. */
  onSaved: () => void;
}

const createDefaults: CurrencyFormValues = {
  isoCode: '',
  name: '',
  symbol: '',
  isActive: true
};

/**
 * Create / edit dialog for an ISO 4217 currency (SDD-NOM-001 §2.1, §2.6). Uses
 * react-hook-form + zod for shape validation; on edit, `isoCode` is immutable (read-only)
 * and the captured `rowVersion` is round-tripped for optimistic concurrency. All API
 * failures surface through the mutation hook's `notification.error(getApiErrorMessage(...))`.
 */
export function CurrencyFormDialog({ open, currency, onClose, onSaved }: CurrencyFormDialogProps) {
  const { t } = useTranslation();
  const { create, update, isSaving } = useCurrencyMutations();
  const isEdit = currency !== null;

  const {
    control,
    handleSubmit,
    reset,
    formState: { errors }
  } = useForm<CurrencyFormValues>({
    resolver: zodResolver(currencyFormSchema),
    defaultValues: createDefaults
  });

  useEffect(() => {
    if (!open) {
      return;
    }
    reset(
      currency
        ? {
            isoCode: currency.isoCode,
            name: currency.name,
            symbol: currency.symbol ?? '',
            isActive: currency.isActive
          }
        : createDefaults
    );
  }, [open, currency, reset]);

  async function onSubmit(values: CurrencyFormValues) {
    const symbol = values.symbol.trim() === '' ? null : values.symbol.trim();

    if (isEdit && currency) {
      const result = await update({
        isoCode: currency.isoCode,
        request: {
          name: values.name,
          symbol,
          isActive: values.isActive,
          rowVersion: currency.rowVersion
        }
      });
      if (result) {
        onSaved();
      }
      return;
    }

    const created = await create({
      isoCode: values.isoCode,
      name: values.name,
      symbol,
      isActive: values.isActive
    });
    if (created) {
      onSaved();
    }
  }

  const fieldError = (key?: string): string | undefined => (key ? t(key) : undefined);

  return (
    <Dialog open={open} onClose={isSaving ? undefined : onClose} maxWidth="sm" fullWidth>
      <DialogContent sx={{ pt: 3 }}>
        <Typography
          component="h2"
          sx={{ fontFamily: serifFamily, fontWeight: 500, fontSize: '1.375rem', mb: 1 }}
        >
          {isEdit ? t('currencies.editTitle') : t('currencies.createTitle')}
        </Typography>
        <Box sx={{ height: '1px', backgroundColor: 'divider', mb: 3 }} />

        <form id="currency-form" onSubmit={handleSubmit(onSubmit)} noValidate>
          <Stack spacing={2.5}>
            <Controller
              name="isoCode"
              control={control}
              render={({ field }) => (
                <FormField
                  label={t('currencies.isoCode')}
                  required
                  error={fieldError(errors.isoCode?.message)}
                >
                  <AppTextField
                    {...field}
                    disabled={isEdit}
                    error={Boolean(errors.isoCode)}
                    autoFocus={!isEdit}
                    onChange={(e) => field.onChange(e.target.value.toUpperCase())}
                    inputProps={{ maxLength: 3, style: { fontFamily: 'inherit' } }}
                  />
                </FormField>
              )}
            />

            <Controller
              name="name"
              control={control}
              render={({ field }) => (
                <FormField
                  label={t('currencies.name')}
                  required
                  error={fieldError(errors.name?.message)}
                >
                  <AppTextField {...field} error={Boolean(errors.name)} autoFocus={isEdit} />
                </FormField>
              )}
            />

            <Controller
              name="symbol"
              control={control}
              render={({ field }) => (
                <FormField label={t('currencies.symbol')} error={fieldError(errors.symbol?.message)}>
                  <AppTextField
                    {...field}
                    error={Boolean(errors.symbol)}
                    inputProps={{ maxLength: 5 }}
                  />
                </FormField>
              )}
            />

            <Controller
              name="isActive"
              control={control}
              render={({ field }) => (
                <FormControlLabel
                  control={
                    <Switch
                      checked={field.value}
                      onChange={(e) => field.onChange(e.target.checked)}
                    />
                  }
                  label={t('currencies.active')}
                />
              )}
            />
          </Stack>
        </form>
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2 }}>
        <AppButton variant="text" onClick={onClose} disabled={isSaving}>
          {t('common.cancel')}
        </AppButton>
        <AppButton type="submit" form="currency-form" variant="contained" disabled={isSaving}>
          {isSaving ? t('common.saving') : t('common.save')}
        </AppButton>
      </DialogActions>
    </Dialog>
  );
}
