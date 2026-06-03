import { useEffect, useMemo } from 'react';
import { Controller, useFieldArray, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import {
  Dialog,
  DialogContent,
  DialogActions,
  MenuItem,
  Stack,
  Box,
  Typography,
  IconButton
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { AppButton, AppTextField, CodeText, MoneyText, StatusDot } from '@/components/atoms';
import { FormField } from '@/components/molecules';
import { useNomenclature } from '@/shared/hooks/useNomenclature';
import { useLayoutStore } from '@/shared/stores/layout';
import { serifFamily } from '@/shared/theme';
import { searchAccounts } from '@/features/accounts/api';
import { useJournalMutations } from '@/features/journal/useJournalMutations';
import {
  baseAmount,
  journalFormSchema,
  type JournalFormValues
} from '@/features/journal/schema';
import type { JournalEntryDto, JournalEntryLineRequest } from '@/features/journal/types';

interface JournalEntryFormDialogProps {
  open: boolean;
  /** The draft entry being edited; `null` opens the create flow. */
  entry: JournalEntryDto | null;
  onClose: () => void;
  /** Called after a successful create/update so the caller can close + refresh. */
  onSaved: () => void;
}

/** A new blank line in the editor (defaults to the base currency at rate 1). */
function blankLine(): JournalFormValues['lines'][number] {
  return {
    accountId: 0,
    currencyCode: '',
    exchangeRate: 1,
    debitAmount: 0,
    creditAmount: 0,
    description: ''
  };
}

function createDefaults(): JournalFormValues {
  return {
    entryDate: new Date().toISOString().slice(0, 10),
    description: '',
    lines: [blankLine(), blankLine()]
  };
}

/** Maps a form line to the wire request, pre-computing the base-currency amounts. */
function toLineRequest(line: JournalFormValues['lines'][number]): JournalEntryLineRequest {
  return {
    accountId: line.accountId,
    currencyCode: line.currencyCode,
    exchangeRate: line.exchangeRate,
    debitAmount: line.debitAmount,
    creditAmount: line.creditAmount,
    baseDebitAmount: baseAmount(line.debitAmount, line.exchangeRate),
    baseCreditAmount: baseAmount(line.creditAmount, line.exchangeRate),
    description: line.description?.trim() ? line.description.trim() : null
  };
}

/**
 * Create / edit dialog for a DRAFT journal entry (SDD-FIN-002 §2.3, §2.5). Header fields plus a
 * dynamic line editor (add/remove; per line an account + currency dropdown, debit XOR credit, and
 * a foreign rate) with live running totals and a balance indicator. Submit is disabled until the
 * entry balances in base currency. Posted/Reversed entries are immutable and never reach this
 * dialog. All API failures surface through the mutation hook's
 * `notification.error(getApiErrorMessage(...))`.
 */
export function JournalEntryFormDialog({
  open,
  entry,
  onClose,
  onSaved
}: JournalEntryFormDialogProps) {
  const { t } = useTranslation();
  const { currencies } = useNomenclature();
  const isCompact = useLayoutStore((s) => s.isCompact);
  const { create, update, isSaving } = useJournalMutations();
  const isEdit = entry !== null;

  const accountsQuery = useQuery({
    queryKey: ['accounts', 'lookup'],
    queryFn: () =>
      searchAccounts({ page: 1, pageSize: 200, sort: [{ field: 'code', direction: 'asc' }] }),
    enabled: open,
    staleTime: 5 * 60 * 1000
  });
  const accounts = useMemo(
    () => (accountsQuery.data?.items ?? []).filter((a) => a.isActive),
    [accountsQuery.data]
  );

  const {
    control,
    handleSubmit,
    reset,
    watch,
    formState: { errors }
  } = useForm<JournalFormValues>({
    resolver: zodResolver(journalFormSchema),
    defaultValues: createDefaults(),
    mode: 'onChange'
  });

  const { fields, append, remove } = useFieldArray({ control, name: 'lines' });

  useEffect(() => {
    if (!open) {
      return;
    }
    reset(
      entry
        ? {
            entryDate: entry.entryDate.slice(0, 10),
            description: entry.description,
            lines: entry.lines.map((line) => ({
              accountId: line.accountId,
              currencyCode: line.currencyCode,
              exchangeRate: line.exchangeRate,
              debitAmount: line.debitAmount,
              creditAmount: line.creditAmount,
              description: line.description ?? ''
            }))
          }
        : createDefaults()
    );
  }, [open, entry, reset]);

  const watchedLines = watch('lines');
  const totalBaseDebit = (watchedLines ?? []).reduce(
    (sum, line) => sum + baseAmount(Number(line.debitAmount) || 0, Number(line.exchangeRate) || 0),
    0
  );
  const totalBaseCredit = (watchedLines ?? []).reduce(
    (sum, line) => sum + baseAmount(Number(line.creditAmount) || 0, Number(line.exchangeRate) || 0),
    0
  );
  const difference = Math.round((totalBaseDebit - totalBaseCredit) * 100) / 100;
  const isBalanced = difference === 0 && totalBaseDebit > 0;

  async function onSubmit(values: JournalFormValues) {
    const lines = values.lines.map(toLineRequest);

    if (isEdit && entry) {
      const result = await update({
        id: entry.id,
        request: {
          entryDate: new Date(values.entryDate).toISOString(),
          description: values.description,
          lines,
          rowVersion: entry.rowVersion
        }
      });
      if (result) {
        onSaved();
      }
      return;
    }

    const created = await create({
      entryDate: new Date(values.entryDate).toISOString(),
      description: values.description,
      lines
    });
    if (created) {
      onSaved();
    }
  }

  const fieldError = (key?: string): string | undefined => (key ? t(key) : undefined);

  return (
    <Dialog open={open} onClose={isSaving ? undefined : onClose} maxWidth="lg" fullWidth>
      <DialogContent sx={{ pt: 3 }}>
        <Typography
          component="h2"
          sx={{ fontFamily: serifFamily, fontWeight: 500, fontSize: '1.375rem', mb: 1 }}
        >
          {isEdit ? t('journal.editTitle') : t('journal.createTitle')}
        </Typography>
        <Box sx={{ height: '1px', backgroundColor: 'divider', mb: 3 }} />

        <form id="journal-form" onSubmit={handleSubmit(onSubmit)} noValidate>
          <Stack spacing={2.5}>
            <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
              <Box sx={{ flex: '0 0 200px' }}>
                <Controller
                  name="entryDate"
                  control={control}
                  render={({ field }) => (
                    <FormField
                      label={t('journal.entryDate')}
                      required
                      error={fieldError(errors.entryDate?.message)}
                    >
                      <AppTextField
                        {...field}
                        type="date"
                        error={Boolean(errors.entryDate)}
                        InputLabelProps={{ shrink: true }}
                      />
                    </FormField>
                  )}
                />
              </Box>
              <Box sx={{ flex: '1 1 320px', minWidth: 240 }}>
                <Controller
                  name="description"
                  control={control}
                  render={({ field }) => (
                    <FormField
                      label={t('journal.description')}
                      required
                      error={fieldError(errors.description?.message)}
                    >
                      <AppTextField
                        {...field}
                        error={Boolean(errors.description)}
                        autoFocus
                      />
                    </FormField>
                  )}
                />
              </Box>
            </Box>

            <Box>
              <Typography variant="overline" component="div" sx={{ mb: 1 }}>
                {t('journal.lines')}
              </Typography>

              <Stack spacing={1.5}>
                {fields.map((row, index) => (
                  <Box
                    key={row.id}
                    sx={{
                      display: 'flex',
                      gap: 1,
                      alignItems: 'flex-start',
                      flexWrap: 'wrap',
                      border: '1px solid',
                      borderColor: 'divider',
                      borderRadius: 1,
                      p: isCompact ? 1.5 : 2
                    }}
                  >
                    <Box sx={{ flex: '2 1 200px', minWidth: 180 }}>
                      <Controller
                        name={`lines.${index}.accountId`}
                        control={control}
                        render={({ field }) => (
                          <FormField
                            label={t('journal.account')}
                            required
                            error={fieldError(errors.lines?.[index]?.accountId?.message)}
                          >
                            <AppTextField
                              select
                              value={field.value ? String(field.value) : ''}
                              disabled={accountsQuery.isLoading}
                              error={Boolean(errors.lines?.[index]?.accountId)}
                              onChange={(e) => field.onChange(Number(e.target.value))}
                            >
                              {accounts.map((account) => (
                                <MenuItem key={account.id} value={String(account.id)}>
                                  <CodeText sx={{ mr: 1 }}>{account.code}</CodeText>
                                  {account.name}
                                </MenuItem>
                              ))}
                            </AppTextField>
                          </FormField>
                        )}
                      />
                    </Box>

                    <Box sx={{ flex: '0 0 110px' }}>
                      <Controller
                        name={`lines.${index}.currencyCode`}
                        control={control}
                        render={({ field }) => (
                          <FormField
                            label={t('journal.currency')}
                            required
                            error={fieldError(errors.lines?.[index]?.currencyCode?.message)}
                          >
                            <AppTextField
                              select
                              value={field.value}
                              error={Boolean(errors.lines?.[index]?.currencyCode)}
                              onChange={(e) => field.onChange(e.target.value)}
                            >
                              {currencies.map((c) => (
                                <MenuItem key={c.code} value={c.code}>
                                  <CodeText>{c.code}</CodeText>
                                </MenuItem>
                              ))}
                            </AppTextField>
                          </FormField>
                        )}
                      />
                    </Box>

                    <Box sx={{ flex: '0 0 120px' }}>
                      <Controller
                        name={`lines.${index}.debitAmount`}
                        control={control}
                        render={({ field }) => (
                          <FormField
                            label={t('journal.debit')}
                            error={fieldError(errors.lines?.[index]?.debitAmount?.message)}
                          >
                            <AppTextField
                              type="number"
                              value={field.value}
                              error={Boolean(errors.lines?.[index]?.debitAmount)}
                              onChange={(e) => field.onChange(Number(e.target.value))}
                              inputProps={{ min: 0, step: '0.01' }}
                            />
                          </FormField>
                        )}
                      />
                    </Box>

                    <Box sx={{ flex: '0 0 120px' }}>
                      <Controller
                        name={`lines.${index}.creditAmount`}
                        control={control}
                        render={({ field }) => (
                          <FormField
                            label={t('journal.credit')}
                            error={fieldError(errors.lines?.[index]?.creditAmount?.message)}
                          >
                            <AppTextField
                              type="number"
                              value={field.value}
                              error={Boolean(errors.lines?.[index]?.creditAmount)}
                              onChange={(e) => field.onChange(Number(e.target.value))}
                              inputProps={{ min: 0, step: '0.01' }}
                            />
                          </FormField>
                        )}
                      />
                    </Box>

                    <Box sx={{ flex: '0 0 110px' }}>
                      <Controller
                        name={`lines.${index}.exchangeRate`}
                        control={control}
                        render={({ field }) => (
                          <FormField
                            label={t('journal.rate')}
                            required
                            error={fieldError(errors.lines?.[index]?.exchangeRate?.message)}
                          >
                            <AppTextField
                              type="number"
                              value={field.value}
                              error={Boolean(errors.lines?.[index]?.exchangeRate)}
                              onChange={(e) => field.onChange(Number(e.target.value))}
                              inputProps={{ min: 0, step: '0.000001' }}
                            />
                          </FormField>
                        )}
                      />
                    </Box>

                    <Box sx={{ flex: '0 0 auto', pt: 3 }}>
                      <IconButton
                        aria-label={t('journal.removeLine')}
                        onClick={() => remove(index)}
                        disabled={fields.length <= 2}
                        size="small"
                        color="error"
                      >
                        <DeleteOutlineIcon fontSize="small" />
                      </IconButton>
                    </Box>
                  </Box>
                ))}
              </Stack>

              {errors.lines?.message ? (
                <Typography variant="caption" sx={{ color: 'error.main', mt: 1, display: 'block' }}>
                  {t(errors.lines.message)}
                </Typography>
              ) : null}

              <AppButton
                variant="text"
                startIcon={<AddIcon />}
                onClick={() => append(blankLine())}
                sx={{ mt: 1 }}
              >
                {t('journal.addLine')}
              </AppButton>
            </Box>

            <Box
              sx={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                gap: 2,
                flexWrap: 'wrap',
                borderTop: '1px solid',
                borderColor: 'divider',
                pt: 2
              }}
            >
              <Box sx={{ display: 'flex', gap: 3, flexWrap: 'wrap' }}>
                <Box>
                  <Typography variant="overline" component="div">
                    {t('journal.totalDebit')}
                  </Typography>
                  <MoneyText amount={totalBaseDebit} />
                </Box>
                <Box>
                  <Typography variant="overline" component="div">
                    {t('journal.totalCredit')}
                  </Typography>
                  <MoneyText amount={totalBaseCredit} />
                </Box>
                <Box>
                  <Typography variant="overline" component="div">
                    {t('journal.difference')}
                  </Typography>
                  <MoneyText amount={difference} />
                </Box>
              </Box>
              <StatusDot
                tone={isBalanced ? 'positive' : 'warning'}
                label={isBalanced ? t('journal.balanced') : t('journal.unbalanced')}
              />
            </Box>
          </Stack>
        </form>
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2 }}>
        <AppButton variant="text" onClick={onClose} disabled={isSaving}>
          {t('common.cancel')}
        </AppButton>
        <AppButton
          type="submit"
          form="journal-form"
          variant="contained"
          disabled={isSaving || !isBalanced}
        >
          {isSaving ? t('common.saving') : t('common.save')}
        </AppButton>
      </DialogActions>
    </Dialog>
  );
}
