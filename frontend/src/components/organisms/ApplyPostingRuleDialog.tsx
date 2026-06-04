import { useEffect, useState } from 'react';
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
import { AppButton, AppTextField, CodeText, MoneyText, StatusDot } from '@/components/atoms';
import { FormField } from '@/components/molecules';
import { useNomenclature } from '@/shared/hooks/useNomenclature';
import { serifFamily } from '@/shared/theme';
import { usePostingRuleMutations } from '@/features/postingRules/usePostingRuleMutations';
import {
  applyPostingRuleFormSchema,
  type ApplyPostingRuleFormValues
} from '@/features/postingRules/schema';
import { PostingAmountSource, type PostingRuleDto } from '@/features/postingRules/types';
import {
  JournalEntryStatus,
  journalStatusLabelKey,
  type JournalEntryDto
} from '@/features/journal/types';

interface ApplyPostingRuleDialogProps {
  /** The rule to apply; `null` keeps the dialog closed. */
  rule: PostingRuleDto | null;
  onClose: () => void;
}

/** The default base currency context (BGN) per SDD-FIN-006 §2.5. */
const DEFAULT_CURRENCY = 'BGN';

function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}

function createDefaults(): ApplyPostingRuleFormValues {
  return {
    net: 0,
    tax: 0,
    gross: 0,
    currencyCode: DEFAULT_CURRENCY,
    entryDate: todayIso(),
    postImmediately: true
  };
}

function statusTone(status: JournalEntryStatus): 'positive' | 'neutral' | 'warning' {
  if (status === JournalEntryStatus.Posted) {
    return 'positive';
  }
  if (status === JournalEntryStatus.Reversed) {
    return 'warning';
  }
  return 'neutral';
}

/**
 * Applies a posting rule to a caller-supplied amount context (SDD-FIN-006 §2.3, §2.5). The caller
 * enters the named amounts (Net / Tax / Gross), a currency (default BGN), an entry date (default
 * today), and whether to post the resulting entry immediately. Submitting calls `POST /posting/apply`,
 * which CREATES A JOURNAL ENTRY — a Draft when post-immediately is off, or a posted entry when on
 * (there is no non-persisting preview). On success the resulting entry's summary (number, status, and
 * its balanced lines) is shown in a result panel. All API failures surface through the mutation hook's
 * `notification.error(getApiErrorMessage(...))`.
 */
export function ApplyPostingRuleDialog({ rule, onClose }: ApplyPostingRuleDialogProps) {
  const { t } = useTranslation();
  const { currencies } = useNomenclature();
  const { apply, isApplying } = usePostingRuleMutations();
  const [result, setResult] = useState<JournalEntryDto | null>(null);

  const open = rule !== null;

  const {
    control,
    handleSubmit,
    reset,
    formState: { errors }
  } = useForm<ApplyPostingRuleFormValues>({
    resolver: zodResolver(applyPostingRuleFormSchema),
    defaultValues: createDefaults()
  });

  useEffect(() => {
    if (open) {
      reset(createDefaults());
      setResult(null);
    }
  }, [open, rule, reset]);

  async function onSubmit(values: ApplyPostingRuleFormValues) {
    if (!rule) {
      return;
    }
    const entry = await apply({
      ruleKey: rule.ruleKey,
      amounts: {
        [String(PostingAmountSource.Net)]: values.net,
        [String(PostingAmountSource.Tax)]: values.tax,
        [String(PostingAmountSource.Gross)]: values.gross
      },
      currencyCode: values.currencyCode.trim().toUpperCase(),
      entryDate: new Date(values.entryDate).toISOString(),
      postImmediately: values.postImmediately
    });
    if (entry) {
      setResult(entry);
    }
  }

  const fieldError = (key?: string): string | undefined => (key ? t(key) : undefined);

  const amountFields: Array<{ name: 'net' | 'tax' | 'gross'; label: string }> = [
    { name: 'net', label: t('postingRules.source_Net') },
    { name: 'tax', label: t('postingRules.source_Tax') },
    { name: 'gross', label: t('postingRules.source_Gross') }
  ];

  return (
    <Dialog open={open} onClose={isApplying ? undefined : onClose} maxWidth="md" fullWidth>
      <DialogContent sx={{ pt: 3 }}>
        <Typography
          component="h2"
          sx={{ fontFamily: serifFamily, fontWeight: 500, fontSize: '1.375rem', mb: 1 }}
        >
          {t('postingRules.applyTitle')}
        </Typography>
        <Box sx={{ height: '1px', backgroundColor: 'divider', mb: 2 }} />

        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 3, flexWrap: 'wrap' }}>
          <Typography variant="overline" component="span">
            {t('postingRules.ruleKey')}
          </Typography>
          <CodeText>{rule?.ruleKey ?? ''}</CodeText>
          <Typography variant="body2" sx={{ color: 'text.secondary' }}>
            {rule?.description ?? ''}
          </Typography>
        </Box>

        <Typography variant="body2" sx={{ color: 'text.secondary', mb: 2 }}>
          {t('postingRules.applyHint')}
        </Typography>

        <form id="apply-posting-rule-form" onSubmit={handleSubmit(onSubmit)} noValidate>
          <Stack spacing={2.5}>
            <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
              {amountFields.map((amount) => (
                <Box key={amount.name} sx={{ flex: '0 0 150px' }}>
                  <Controller
                    name={amount.name}
                    control={control}
                    render={({ field }) => (
                      <FormField label={amount.label} error={fieldError(errors[amount.name]?.message)}>
                        <AppTextField
                          type="number"
                          value={field.value}
                          error={Boolean(errors[amount.name])}
                          onChange={(e) => field.onChange(Number(e.target.value))}
                          inputProps={{ min: 0, step: '0.01' }}
                        />
                      </FormField>
                    )}
                  />
                </Box>
              ))}
            </Box>

            <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
              <Box sx={{ flex: '0 0 150px' }}>
                <Controller
                  name="currencyCode"
                  control={control}
                  render={({ field }) => (
                    <FormField
                      label={t('postingRules.currency')}
                      required
                      error={fieldError(errors.currencyCode?.message)}
                    >
                      <AppTextField
                        select
                        value={field.value}
                        error={Boolean(errors.currencyCode)}
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
              <Box sx={{ flex: '0 0 200px' }}>
                <Controller
                  name="entryDate"
                  control={control}
                  render={({ field }) => (
                    <FormField
                      label={t('postingRules.entryDate')}
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
            </Box>

            <Controller
              name="postImmediately"
              control={control}
              render={({ field }) => (
                <FormControlLabel
                  control={
                    <Switch
                      checked={field.value}
                      onChange={(e) => field.onChange(e.target.checked)}
                    />
                  }
                  label={t('postingRules.postImmediately')}
                />
              )}
            />
          </Stack>
        </form>

        {result ? (
          <Box
            sx={{
              mt: 3,
              border: '1px solid',
              borderColor: 'divider',
              borderRadius: 1,
              p: 2
            }}
          >
            <Box
              sx={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                gap: 2,
                flexWrap: 'wrap',
                mb: 1.5
              }}
            >
              <Typography variant="overline" component="div">
                {t('postingRules.resultHeading')}
              </Typography>
              <StatusDot
                tone={statusTone(result.status)}
                label={t(journalStatusLabelKey(result.status))}
              />
            </Box>

            <Box sx={{ display: 'flex', gap: 3, flexWrap: 'wrap', mb: 2 }}>
              <Box>
                <Typography variant="overline" component="div">
                  {t('postingRules.resultEntryNumber')}
                </Typography>
                <CodeText>{result.entryNumber ?? t('postingRules.resultDraftNumber')}</CodeText>
              </Box>
              <Box>
                <Typography variant="overline" component="div">
                  {t('postingRules.entryDate')}
                </Typography>
                <CodeText>{result.entryDate.slice(0, 10)}</CodeText>
              </Box>
              <Box>
                <Typography variant="overline" component="div">
                  {t('journal.baseCurrency')}
                </Typography>
                <CodeText>{result.baseCurrencyCode}</CodeText>
              </Box>
            </Box>

            <Stack spacing={0.75}>
              {result.lines.map((line) => (
                <Box
                  key={line.id}
                  sx={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: 2,
                    flexWrap: 'wrap',
                    borderTop: '1px solid',
                    borderColor: 'divider',
                    pt: 0.75
                  }}
                >
                  <Box sx={{ flex: '0 0 40px' }}>
                    <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                      {line.lineNumber}
                    </Typography>
                  </Box>
                  <Box sx={{ flex: '1 1 120px' }}>
                    <CodeText>{line.accountId}</CodeText>
                  </Box>
                  <Box sx={{ flex: '0 0 140px', textAlign: 'right' }}>
                    <MoneyText amount={line.baseDebitAmount} />
                  </Box>
                  <Box sx={{ flex: '0 0 140px', textAlign: 'right' }}>
                    <MoneyText amount={line.baseCreditAmount} />
                  </Box>
                </Box>
              ))}
            </Stack>
          </Box>
        ) : null}
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2 }}>
        <AppButton variant="text" onClick={onClose} disabled={isApplying}>
          {result ? t('common.close') : t('common.cancel')}
        </AppButton>
        {!result ? (
          <AppButton
            type="submit"
            form="apply-posting-rule-form"
            variant="contained"
            disabled={isApplying}
          >
            {isApplying ? t('postingRules.applying') : t('postingRules.apply')}
          </AppButton>
        ) : null}
      </DialogActions>
    </Dialog>
  );
}
