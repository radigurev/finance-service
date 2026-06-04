import { useEffect } from 'react';
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
  IconButton,
  FormControlLabel,
  Switch
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import { useTranslation } from 'react-i18next';
import { AppButton, AppTextField, StatusDot } from '@/components/atoms';
import { FormField } from '@/components/molecules';
import { useLayoutStore } from '@/shared/stores/layout';
import { serifFamily } from '@/shared/theme';
import { usePostingRuleMutations } from '@/features/postingRules/usePostingRuleMutations';
import {
  postingRuleFormSchema,
  type PostingRuleFormValues
} from '@/features/postingRules/schema';
import {
  AMOUNT_SOURCES,
  DEBIT_OR_CREDIT,
  PostingAmountSource,
  PostingDebitOrCredit,
  amountSourceLabelKey,
  debitOrCreditLabelKey,
  type CreatePostingRuleLineRequest,
  type PostingRuleDto
} from '@/features/postingRules/types';

interface PostingRuleFormDialogProps {
  open: boolean;
  /** The rule being edited; `null` opens the create flow. */
  rule: PostingRuleDto | null;
  onClose: () => void;
  /** Called after a successful create/update so the caller can close + refresh. */
  onSaved: () => void;
}

/** A new blank line in the editor — defaults to a debit drawing the net amount. */
function blankLine(): PostingRuleFormValues['lines'][number] {
  return {
    accountSelector: '',
    debitOrCredit: PostingDebitOrCredit.Debit,
    amountSource: PostingAmountSource.Net
  };
}

function createDefaults(): PostingRuleFormValues {
  return {
    ruleKey: '',
    description: '',
    isActive: true,
    lines: [blankLine(), blankLine()]
  };
}

/** Maps a form line to the wire request shape (identical fields). */
function toLineRequest(line: PostingRuleFormValues['lines'][number]): CreatePostingRuleLineRequest {
  return {
    accountSelector: line.accountSelector.trim(),
    debitOrCredit: line.debitOrCredit,
    amountSource: line.amountSource
  };
}

/**
 * Create / edit dialog for a posting rule (SDD-FIN-006 §2.1). A header (rule key — mono, immutable
 * on edit; description; active toggle on edit) plus an ordered dynamic line editor: per line an
 * account-code field, a debit/credit select, and an amount-source select, with add/remove rows and
 * a minimum of one line. An inline balanceability hint warns until the lines carry at least one
 * debit AND one credit. Submit POSTs (create) or PUTs (edit, round-tripping `rowVersion` for
 * optimistic concurrency → `CONCURRENT_MODIFICATION`). All API failures surface through the mutation
 * hook's `notification.error(getApiErrorMessage(...))`.
 */
export function PostingRuleFormDialog({ open, rule, onClose, onSaved }: PostingRuleFormDialogProps) {
  const { t } = useTranslation();
  const isCompact = useLayoutStore((s) => s.isCompact);
  const { create, update, isSaving } = usePostingRuleMutations();
  const isEdit = rule !== null;

  const {
    control,
    handleSubmit,
    reset,
    watch,
    formState: { errors }
  } = useForm<PostingRuleFormValues>({
    resolver: zodResolver(postingRuleFormSchema),
    defaultValues: createDefaults(),
    mode: 'onChange'
  });

  const { fields, append, remove } = useFieldArray({ control, name: 'lines' });

  useEffect(() => {
    if (!open) {
      return;
    }
    reset(
      rule
        ? {
            ruleKey: rule.ruleKey,
            description: rule.description,
            isActive: rule.isActive,
            lines: rule.lines.map((line) => ({
              accountSelector: line.accountSelector,
              debitOrCredit: line.debitOrCredit,
              amountSource: line.amountSource
            }))
          }
        : createDefaults()
    );
  }, [open, rule, reset]);

  const watchedLines = watch('lines') ?? [];
  const hasDebit = watchedLines.some((line) => line.debitOrCredit === PostingDebitOrCredit.Debit);
  const hasCredit = watchedLines.some((line) => line.debitOrCredit === PostingDebitOrCredit.Credit);
  const isBalanceable = hasDebit && hasCredit;

  async function onSubmit(values: PostingRuleFormValues) {
    const lines = values.lines.map(toLineRequest);

    if (isEdit && rule) {
      const result = await update({
        id: rule.id,
        request: {
          description: values.description,
          isActive: values.isActive,
          lines,
          rowVersion: rule.rowVersion
        }
      });
      if (result) {
        onSaved();
      }
      return;
    }

    const created = await create({
      ruleKey: values.ruleKey,
      description: values.description,
      lines
    });
    if (created) {
      onSaved();
    }
  }

  const fieldError = (key?: string): string | undefined => (key ? t(key) : undefined);

  return (
    <Dialog open={open} onClose={isSaving ? undefined : onClose} maxWidth="md" fullWidth>
      <DialogContent sx={{ pt: 3 }}>
        <Typography
          component="h2"
          sx={{ fontFamily: serifFamily, fontWeight: 500, fontSize: '1.375rem', mb: 1 }}
        >
          {isEdit ? t('postingRules.editTitle') : t('postingRules.createTitle')}
        </Typography>
        <Box sx={{ height: '1px', backgroundColor: 'divider', mb: 3 }} />

        <form id="posting-rule-form" onSubmit={handleSubmit(onSubmit)} noValidate>
          <Stack spacing={2.5}>
            <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap', alignItems: 'flex-start' }}>
              <Box sx={{ flex: '0 0 240px' }}>
                <Controller
                  name="ruleKey"
                  control={control}
                  render={({ field }) => (
                    <FormField
                      label={t('postingRules.ruleKey')}
                      required
                      error={fieldError(errors.ruleKey?.message)}
                    >
                      <AppTextField
                        {...field}
                        disabled={isEdit}
                        error={Boolean(errors.ruleKey)}
                        autoFocus={!isEdit}
                        onChange={(e) => field.onChange(e.target.value.toUpperCase())}
                        inputProps={{ maxLength: 50, style: { fontFamily: 'inherit' } }}
                      />
                    </FormField>
                  )}
                />
              </Box>
              <Box sx={{ flex: '1 1 280px', minWidth: 220 }}>
                <Controller
                  name="description"
                  control={control}
                  render={({ field }) => (
                    <FormField
                      label={t('postingRules.description')}
                      required
                      error={fieldError(errors.description?.message)}
                    >
                      <AppTextField
                        {...field}
                        error={Boolean(errors.description)}
                        autoFocus={isEdit}
                      />
                    </FormField>
                  )}
                />
              </Box>
            </Box>

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
                    label={t('postingRules.active')}
                  />
                )}
              />
            ) : null}

            <Box>
              <Typography variant="overline" component="div" sx={{ mb: 1 }}>
                {t('postingRules.lines')}
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
                    <Box sx={{ flex: '0 0 32px', pt: 3 }}>
                      <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                        {index + 1}
                      </Typography>
                    </Box>

                    <Box sx={{ flex: '2 1 200px', minWidth: 180 }}>
                      <Controller
                        name={`lines.${index}.accountSelector`}
                        control={control}
                        render={({ field }) => (
                          <FormField
                            label={t('postingRules.accountSelector')}
                            required
                            error={fieldError(errors.lines?.[index]?.accountSelector?.message)}
                          >
                            <AppTextField
                              {...field}
                              error={Boolean(errors.lines?.[index]?.accountSelector)}
                              inputProps={{ maxLength: 20, style: { fontFamily: 'inherit' } }}
                              placeholder={t('postingRules.accountSelectorPlaceholder')}
                            />
                          </FormField>
                        )}
                      />
                    </Box>

                    <Box sx={{ flex: '0 0 150px' }}>
                      <Controller
                        name={`lines.${index}.debitOrCredit`}
                        control={control}
                        render={({ field }) => (
                          <FormField
                            label={t('postingRules.side')}
                            required
                            error={fieldError(errors.lines?.[index]?.debitOrCredit?.message)}
                          >
                            <AppTextField
                              select
                              value={String(field.value)}
                              error={Boolean(errors.lines?.[index]?.debitOrCredit)}
                              onChange={(e) => field.onChange(Number(e.target.value))}
                            >
                              {DEBIT_OR_CREDIT.map((side) => (
                                <MenuItem key={side} value={String(side)}>
                                  {t(debitOrCreditLabelKey(side))}
                                </MenuItem>
                              ))}
                            </AppTextField>
                          </FormField>
                        )}
                      />
                    </Box>

                    <Box sx={{ flex: '0 0 170px' }}>
                      <Controller
                        name={`lines.${index}.amountSource`}
                        control={control}
                        render={({ field }) => (
                          <FormField
                            label={t('postingRules.amountSource')}
                            required
                            error={fieldError(errors.lines?.[index]?.amountSource?.message)}
                          >
                            <AppTextField
                              select
                              value={String(field.value)}
                              error={Boolean(errors.lines?.[index]?.amountSource)}
                              onChange={(e) => field.onChange(Number(e.target.value))}
                            >
                              {AMOUNT_SOURCES.map((source) => (
                                <MenuItem key={source} value={String(source)}>
                                  {t(amountSourceLabelKey(source))}
                                </MenuItem>
                              ))}
                            </AppTextField>
                          </FormField>
                        )}
                      />
                    </Box>

                    <Box sx={{ flex: '0 0 auto', pt: 3 }}>
                      <IconButton
                        aria-label={t('postingRules.removeLine')}
                        onClick={() => remove(index)}
                        disabled={fields.length <= 1}
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

              <Box
                sx={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  gap: 2,
                  flexWrap: 'wrap',
                  mt: 1.5
                }}
              >
                <AppButton variant="text" startIcon={<AddIcon />} onClick={() => append(blankLine())}>
                  {t('postingRules.addLine')}
                </AppButton>
                <StatusDot
                  tone={isBalanceable ? 'positive' : 'warning'}
                  label={
                    isBalanceable
                      ? t('postingRules.balanceable')
                      : t('postingRules.notBalanceableHint')
                  }
                />
              </Box>
            </Box>
          </Stack>
        </form>
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2 }}>
        <AppButton variant="text" onClick={onClose} disabled={isSaving}>
          {t('common.cancel')}
        </AppButton>
        <AppButton type="submit" form="posting-rule-form" variant="contained" disabled={isSaving}>
          {isSaving ? t('common.saving') : t('common.save')}
        </AppButton>
      </DialogActions>
    </Dialog>
  );
}
