import { useEffect, useMemo } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import {
  Dialog,
  DialogContent,
  DialogActions,
  MenuItem,
  Stack,
  Box,
  Typography
} from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { AppButton, AppTextField, CodeText, MoneyText } from '@/components/atoms';
import { FormField } from '@/components/molecules';
import { useNomenclature } from '@/shared/hooks/useNomenclature';
import { useLayoutStore } from '@/shared/stores/layout';
import { serifFamily } from '@/shared/theme';
import { MAX_PAGE_SIZE } from '@/shared/api/paging';
import { searchAccounts } from '@/features/accounts/api';
import { usePaymentMutations } from '@/features/payments/usePaymentMutations';
import {
  paymentFormSchema,
  previewBaseAmount,
  todayIso,
  type PaymentFormValues
} from '@/features/payments/schema';
import {
  PAYMENT_DOCUMENT_TYPES,
  PAYMENT_METHODS,
  PaymentDocumentType,
  PaymentMethod,
  derivedDirection,
  directionLabelKey,
  documentTypeLabelKey,
  methodLabelKey,
  type PaymentDto
} from '@/features/payments/types';

interface PaymentFormDialogProps {
  open: boolean;
  /** The draft payment being edited; `null` opens the create flow. */
  payment: PaymentDto | null;
  onClose: () => void;
  /** Called after a successful create/update so the caller can close + refresh. */
  onSaved: () => void;
}

/** Blank create defaults: a customer receipt, today's date, and a rate of exactly one. */
function createDefaults(): PaymentFormValues {
  return {
    documentType: PaymentDocumentType.CustomerReceipt,
    method: PaymentMethod.BankTransfer,
    counterpartyId: '',
    currencyCode: '',
    amount: 0,
    exchangeRate: 1,
    settlementAccountId: 0,
    paymentDate: todayIso(),
    bankReference: ''
  };
}

/**
 * Create / edit dialog for a DRAFT payment (SDD-UI-FIN-002 §2.3, §2.5). Carries the document type,
 * method, counterparty, currency, amount, exchange rate, settlement account, payment date, and the
 * optional bank reference.
 *
 * Four shipped-contract details drive the shape of this form:
 *
 * - **`direction` / `baseCurrencyCode` / `baseAmount` are NEVER sent.** All three are server-derived
 *   and `CreatePaymentRequest` does not declare them. The derived direction is DISPLAYED read-only
 *   (`CustomerReceipt → AR`, `SupplierPayment → AP`) so the operator sees the consequence of the
 *   type choice (§2.3).
 * - **`documentType` is read-only in edit mode and sent UNCHANGED.** `UpdatePaymentRequest` carries
 *   it precisely so the server can reject a change — it drives the direction, the sequence key, and
 *   the posting rule (§2.5).
 * - **The base-amount figure is a PREVIEW only.** `amount × rate` rounded to two decimals is
 *   recomputed from the watched fields for immediate feedback, but the country strategy owns the
 *   rounding, so the persisted server `baseAmount` is re-displayed in edit mode and after save. The
 *   preview never blocks submission (§2.3, §3.4).
 * - **The settlement-account picker can only offer the WHOLE chart of accounts.** There is no
 *   "list cash/bank accounts" endpoint and no `IsCash`/`IsPostable` flag on the CoA, so pre-filtering
 *   is impossible in v1; a wrong pick is caught by `PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND` /
 *   `_INACTIVE` at create or confirm (§1.6 gap 2).
 *
 * The rate-equals-one rule is a SOFT hint, never a blocking client rule: the base currency is not
 * readable before a payment exists, so `INVALID_PAYMENT_EXCHANGE_RATE` from the server is the
 * authority (§1.6 gap 3, §3.4). All API failures surface through the mutation hook's
 * `notification.error(getApiErrorMessage(...))`.
 */
export function PaymentFormDialog({ open, payment, onClose, onSaved }: PaymentFormDialogProps) {
  const { t } = useTranslation();
  const { currencies } = useNomenclature();
  const isCompact = useLayoutStore((s) => s.isCompact);
  const { create, update, isSaving } = usePaymentMutations();
  const isEdit: boolean = payment !== null;

  const {
    control,
    handleSubmit,
    reset,
    formState: { errors }
  } = useForm<PaymentFormValues>({
    resolver: zodResolver(paymentFormSchema),
    defaultValues: createDefaults(),
    mode: 'onChange'
  });

  const accountsQuery = useQuery({
    queryKey: ['accounts', 'settlement-picker'],
    queryFn: () =>
      searchAccounts({ page: 1, pageSize: MAX_PAGE_SIZE, sort: [{ field: 'code', direction: 'asc' }] }),
    enabled: open,
    staleTime: 5 * 60 * 1000
  });

  useEffect(() => {
    if (!open) {
      return;
    }
    reset(
      payment
        ? {
            documentType: payment.documentType,
            method: payment.method,
            counterpartyId: payment.counterpartyId,
            currencyCode: payment.currencyCode,
            amount: payment.amount,
            exchangeRate: payment.exchangeRate,
            settlementAccountId: payment.settlementAccountId,
            paymentDate: payment.paymentDate.slice(0, 10),
            bankReference: payment.bankReference ?? ''
          }
        : createDefaults()
    );
  }, [open, payment, reset]);

  const watchedDocumentType = useWatch({ control, name: 'documentType' });
  const watchedAmount = useWatch({ control, name: 'amount' });
  const watchedRate = useWatch({ control, name: 'exchangeRate' });
  const watchedCurrency = useWatch({ control, name: 'currencyCode' });

  const preview: number = useMemo(
    () => previewBaseAmount(Number(watchedAmount), Number(watchedRate)),
    [watchedAmount, watchedRate]
  );

  /**
   * The base currency is only ever exposed ON A RESPONSE, so it is known in edit mode only. When it
   * is known and the payment is in base currency, a rate other than exactly one gets a quiet hint —
   * never a blocking error (§1.6 gap 3).
   */
  const knownBaseCurrency: string | null = payment?.baseCurrencyCode ?? null;
  const showRateHint: boolean =
    knownBaseCurrency !== null &&
    watchedCurrency === knownBaseCurrency &&
    Number(watchedRate) !== 1;

  /**
   * The currency options, guaranteeing the payment's OWN currency is selectable even when the
   * nomenclature list does not carry it (e.g. it was deactivated after the draft was recorded).
   * Without this the select would silently show a blank value for a persisted draft.
   */
  const currencyOptions: string[] = useMemo(() => {
    const codes: string[] = currencies.map((c) => c.code);
    if (payment && payment.currencyCode && !codes.includes(payment.currencyCode)) {
      return [payment.currencyCode, ...codes];
    }
    return codes;
  }, [currencies, payment]);

  /**
   * The settlement-account options. The picker can only offer the WHOLE chart of accounts (§1.6 gap 2),
   * and the loaded page is capped at `MAX_PAGE_SIZE`, so the payment's own account is added when it
   * falls outside that page — again so an existing draft never renders a blank selection.
   */
  const accountOptions: { id: number; code: string; name: string }[] = useMemo(() => {
    const items = accountsQuery.data?.items ?? [];
    const options = items.map((account) => ({
      id: account.id,
      code: account.code,
      name: account.name
    }));
    if (
      payment &&
      payment.settlementAccountId > 0 &&
      !options.some((option) => option.id === payment.settlementAccountId)
    ) {
      return [
        { id: payment.settlementAccountId, code: `#${payment.settlementAccountId}`, name: '' },
        ...options
      ];
    }
    return options;
  }, [accountsQuery.data, payment]);

  async function onSubmit(values: PaymentFormValues) {
    const bankReference: string | null =
      values.bankReference.trim() === '' ? null : values.bankReference.trim();

    if (isEdit && payment) {
      const result: PaymentDto | null = await update({
        id: payment.id,
        request: {
          documentType: payment.documentType,
          method: values.method,
          counterpartyId: values.counterpartyId,
          currencyCode: values.currencyCode,
          amount: values.amount,
          exchangeRate: values.exchangeRate,
          settlementAccountId: values.settlementAccountId,
          paymentDate: new Date(values.paymentDate).toISOString(),
          bankReference,
          rowVersion: payment.rowVersion
        }
      });
      if (result) {
        onSaved();
      }
      return;
    }

    const created: PaymentDto | null = await create({
      documentType: values.documentType,
      method: values.method,
      counterpartyId: values.counterpartyId,
      currencyCode: values.currencyCode,
      amount: values.amount,
      exchangeRate: values.exchangeRate,
      settlementAccountId: values.settlementAccountId,
      paymentDate: new Date(values.paymentDate).toISOString(),
      bankReference
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
          {isEdit ? t('payments.editTitle') : t('payments.createTitle')}
        </Typography>
        <Box sx={{ height: '1px', backgroundColor: 'divider', mb: 3 }} />

        <form id="payment-form" onSubmit={handleSubmit(onSubmit)} noValidate>
          <Stack spacing={2.5}>
            <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
              <Box sx={{ flex: '0 0 220px' }}>
                <Controller
                  name="documentType"
                  control={control}
                  render={({ field }) => (
                    <FormField
                      label={t('payments.documentType')}
                      required
                      error={fieldError(errors.documentType?.message)}
                    >
                      <AppTextField
                        select
                        value={String(field.value)}
                        disabled={isEdit}
                        error={Boolean(errors.documentType)}
                        onChange={(e) => field.onChange(Number(e.target.value))}
                      >
                        {PAYMENT_DOCUMENT_TYPES.map((dt) => (
                          <MenuItem key={dt} value={String(dt)}>
                            {t(documentTypeLabelKey(dt))}
                          </MenuItem>
                        ))}
                      </AppTextField>
                    </FormField>
                  )}
                />
              </Box>

              <Box sx={{ flex: '0 0 120px' }}>
                <FormField label={t('payments.direction')}>
                  <AppTextField
                    value={t(directionLabelKey(derivedDirection(watchedDocumentType)))}
                    inputProps={{ readOnly: true, 'aria-readonly': true }}
                  />
                </FormField>
              </Box>

              <Box sx={{ flex: '0 0 200px' }}>
                <Controller
                  name="method"
                  control={control}
                  render={({ field }) => (
                    <FormField
                      label={t('payments.method')}
                      required
                      error={fieldError(errors.method?.message)}
                    >
                      <AppTextField
                        select
                        value={String(field.value)}
                        error={Boolean(errors.method)}
                        onChange={(e) => field.onChange(Number(e.target.value))}
                      >
                        {PAYMENT_METHODS.map((pm) => (
                          <MenuItem key={pm} value={String(pm)}>
                            {t(methodLabelKey(pm))}
                          </MenuItem>
                        ))}
                      </AppTextField>
                    </FormField>
                  )}
                />
              </Box>

              <Box sx={{ flex: '1 1 300px', minWidth: 260 }}>
                <Controller
                  name="counterpartyId"
                  control={control}
                  render={({ field }) => (
                    <FormField
                      label={t('payments.counterparty')}
                      required
                      error={fieldError(errors.counterpartyId?.message)}
                    >
                      <AppTextField
                        {...field}
                        error={Boolean(errors.counterpartyId)}
                        placeholder={t('payments.counterpartyPlaceholder')}
                      />
                    </FormField>
                  )}
                />
              </Box>
            </Box>

            <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
              <Box sx={{ flex: '0 0 130px' }}>
                <Controller
                  name="currencyCode"
                  control={control}
                  render={({ field }) => (
                    <FormField
                      label={t('payments.currency')}
                      required
                      error={fieldError(errors.currencyCode?.message)}
                    >
                      <AppTextField
                        select
                        value={field.value}
                        error={Boolean(errors.currencyCode)}
                        onChange={(e) => field.onChange(e.target.value)}
                      >
                        {currencyOptions.map((code) => (
                          <MenuItem key={code} value={code}>
                            <CodeText>{code}</CodeText>
                          </MenuItem>
                        ))}
                      </AppTextField>
                    </FormField>
                  )}
                />
              </Box>

              <Box sx={{ flex: '0 0 160px' }}>
                <Controller
                  name="amount"
                  control={control}
                  render={({ field }) => (
                    <FormField
                      label={t('payments.amount')}
                      required
                      error={fieldError(errors.amount?.message)}
                    >
                      <AppTextField
                        type="number"
                        value={field.value}
                        error={Boolean(errors.amount)}
                        onChange={(e) => field.onChange(Number(e.target.value))}
                        inputProps={{ min: 0, step: '0.01' }}
                      />
                    </FormField>
                  )}
                />
              </Box>

              <Box sx={{ flex: '0 0 170px' }}>
                <Controller
                  name="exchangeRate"
                  control={control}
                  render={({ field }) => (
                    <FormField
                      label={t('payments.exchangeRate')}
                      required
                      error={fieldError(errors.exchangeRate?.message)}
                    >
                      <AppTextField
                        type="number"
                        value={field.value}
                        error={Boolean(errors.exchangeRate)}
                        onChange={(e) => field.onChange(Number(e.target.value))}
                        inputProps={{ min: 0, step: '0.000001' }}
                      />
                    </FormField>
                  )}
                />
              </Box>

              <Box sx={{ flex: '0 0 190px' }}>
                <Controller
                  name="paymentDate"
                  control={control}
                  render={({ field }) => (
                    <FormField
                      label={t('payments.paymentDate')}
                      required
                      error={fieldError(errors.paymentDate?.message)}
                    >
                      <AppTextField
                        {...field}
                        type="date"
                        error={Boolean(errors.paymentDate)}
                        InputLabelProps={{ shrink: true }}
                      />
                    </FormField>
                  )}
                />
              </Box>
            </Box>

            {showRateHint ? (
              <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                {t('payments.rateEqualsOneHint', { currency: knownBaseCurrency ?? '' })}
              </Typography>
            ) : null}

            <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
              <Box sx={{ flex: '1 1 320px', minWidth: 260 }}>
                <Controller
                  name="settlementAccountId"
                  control={control}
                  render={({ field }) => (
                    <FormField
                      label={t('payments.settlementAccount')}
                      required
                      error={fieldError(errors.settlementAccountId?.message)}
                    >
                      <AppTextField
                        select
                        value={field.value === 0 ? '' : String(field.value)}
                        error={Boolean(errors.settlementAccountId)}
                        onChange={(e) => field.onChange(Number(e.target.value))}
                      >
                        {accountOptions.map((account) => (
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

              <Box sx={{ flex: '1 1 240px', minWidth: 220 }}>
                <Controller
                  name="bankReference"
                  control={control}
                  render={({ field }) => (
                    <FormField
                      label={t('payments.bankReference')}
                      error={fieldError(errors.bankReference?.message)}
                    >
                      <AppTextField {...field} error={Boolean(errors.bankReference)} />
                    </FormField>
                  )}
                />
              </Box>
            </Box>

            <Box
              sx={{
                display: 'flex',
                flexDirection: 'column',
                gap: 1,
                borderTop: '1px solid',
                borderColor: 'divider',
                pt: isCompact ? 1.5 : 2
              }}
            >
              <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                {t('payments.baseAmountPreview')}
              </Typography>
              <Box sx={{ display: 'flex', gap: 3, flexWrap: 'wrap' }}>
                <Box>
                  <Typography variant="overline" component="div">
                    {t('payments.baseAmount')}
                  </Typography>
                  <MoneyText amount={payment ? payment.baseAmount : preview} />
                </Box>
                {payment ? (
                  <Box>
                    <Typography variant="overline" component="div">
                      {t('payments.baseCurrency')}
                    </Typography>
                    <CodeText>{payment.baseCurrencyCode}</CodeText>
                  </Box>
                ) : null}
              </Box>
            </Box>
          </Stack>
        </form>
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2 }}>
        <AppButton variant="text" onClick={onClose} disabled={isSaving}>
          {t('common.cancel')}
        </AppButton>
        <AppButton type="submit" form="payment-form" variant="contained" disabled={isSaving}>
          {isSaving ? t('common.saving') : t('common.save')}
        </AppButton>
      </DialogActions>
    </Dialog>
  );
}
