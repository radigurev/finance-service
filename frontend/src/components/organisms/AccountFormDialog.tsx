import { useEffect } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import {
  Dialog,
  DialogContent,
  DialogActions,
  MenuItem,
  Stack,
  Box,
  Typography,
  FormControlLabel,
  Switch
} from '@mui/material';
import { useTranslation } from 'react-i18next';
import { AppButton, AppTextField } from '@/components/atoms';
import { FormField } from '@/components/molecules';
import { useNomenclature } from '@/shared/hooks/useNomenclature';
import { serifFamily } from '@/shared/theme';
import { useAccountMutations } from '@/features/accounts/useAccountMutations';
import { accountFormSchema, type AccountFormValues } from '@/features/accounts/schema';
import {
  ACCOUNT_TYPES,
  AccountType,
  accountTypeLabelKey,
  type AccountDto
} from '@/features/accounts/types';

interface AccountFormDialogProps {
  open: boolean;
  /** The account being edited; `null` opens the create flow. */
  account: AccountDto | null;
  onClose: () => void;
  /** Called after a successful create/update so the caller can close + refresh. */
  onSaved: () => void;
}

const createDefaults: AccountFormValues = {
  code: '',
  name: '',
  type: AccountType.Asset,
  parentId: null,
  isActive: true
};

/**
 * Create / edit dialog for a chart-of-accounts entry. Uses react-hook-form + zod for
 * shape validation; on edit, `code` and `type` are immutable and the captured
 * `rowVersion` is round-tripped for optimistic concurrency. All API failures surface
 * through the mutation hook's `notification.error(getApiErrorMessage(...))`.
 */
export function AccountFormDialog({ open, account, onClose, onSaved }: AccountFormDialogProps) {
  const { t } = useTranslation();
  const { countries } = useNomenclature();
  const { create, update, isSaving } = useAccountMutations();
  const isEdit = account !== null;

  const {
    control,
    handleSubmit,
    reset,
    formState: { errors }
  } = useForm<AccountFormValues>({
    resolver: zodResolver(accountFormSchema),
    defaultValues: createDefaults
  });

  useEffect(() => {
    if (!open) {
      return;
    }
    reset(
      account
        ? {
            code: account.code,
            name: account.name,
            type: account.type,
            parentId: account.parentId,
            isActive: account.isActive
          }
        : createDefaults
    );
  }, [open, account, reset]);

  async function onSubmit(values: AccountFormValues) {
    if (isEdit && account) {
      const result = await update({
        id: account.id,
        request: { name: values.name, isActive: values.isActive, rowVersion: account.rowVersion }
      });
      if (result) {
        onSaved();
      }
      return;
    }

    const created = await create({
      code: values.code,
      name: values.name,
      type: values.type,
      parentId: values.parentId
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
          {isEdit ? t('accounts.editTitle') : t('accounts.createTitle')}
        </Typography>
        <Box sx={{ height: '1px', backgroundColor: 'divider', mb: 3 }} />

        <form id="account-form" onSubmit={handleSubmit(onSubmit)} noValidate>
          <Stack spacing={2.5}>
            <Controller
              name="code"
              control={control}
              render={({ field }) => (
                <FormField label={t('accounts.code')} required error={fieldError(errors.code?.message)}>
                  <AppTextField
                    {...field}
                    disabled={isEdit}
                    error={Boolean(errors.code)}
                    autoFocus={!isEdit}
                    inputProps={{ style: { fontFamily: 'inherit' } }}
                  />
                </FormField>
              )}
            />

            <Controller
              name="name"
              control={control}
              render={({ field }) => (
                <FormField label={t('accounts.name')} required error={fieldError(errors.name?.message)}>
                  <AppTextField {...field} error={Boolean(errors.name)} autoFocus={isEdit} />
                </FormField>
              )}
            />

            <Controller
              name="type"
              control={control}
              render={({ field }) => (
                <FormField label={t('accounts.type')} required error={fieldError(errors.type?.message)}>
                  <AppTextField
                    {...field}
                    select
                    disabled={isEdit}
                    error={Boolean(errors.type)}
                    value={field.value}
                    onChange={(e) => field.onChange(Number(e.target.value) as AccountType)}
                  >
                    {ACCOUNT_TYPES.map((type) => (
                      <MenuItem key={type} value={type}>
                        {t(accountTypeLabelKey(type))}
                      </MenuItem>
                    ))}
                  </AppTextField>
                </FormField>
              )}
            />

            {isEdit && account ? (
              <FormField label={t('accounts.country')}>
                <AppTextField
                  value={
                    countries.find((c) => c.code === account.countryCode)?.name ?? account.countryCode
                  }
                  disabled
                />
              </FormField>
            ) : null}

            {isEdit ? (
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
                    label={t('accounts.active')}
                  />
                )}
              />
            ) : null}
          </Stack>
        </form>
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2 }}>
        <AppButton variant="text" onClick={onClose} disabled={isSaving}>
          {t('common.cancel')}
        </AppButton>
        <AppButton type="submit" form="account-form" variant="contained" disabled={isSaving}>
          {isSaving ? t('common.saving') : t('common.save')}
        </AppButton>
      </DialogActions>
    </Dialog>
  );
}
