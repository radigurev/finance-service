import { useEffect, useMemo } from 'react';
import { Controller, useFieldArray, useForm, useWatch } from 'react-hook-form';
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
import { useTranslation } from 'react-i18next';
import { AppButton, AppTextField, CodeText, MoneyText } from '@/components/atoms';
import { FormField } from '@/components/molecules';
import { useNomenclature } from '@/shared/hooks/useNomenclature';
import { useLayoutStore } from '@/shared/stores/layout';
import { serifFamily } from '@/shared/theme';
import { useInvoiceMutations } from '@/features/invoices/useInvoiceMutations';
import {
  invoiceFormSchema,
  previewTotals,
  type InvoiceFormValues
} from '@/features/invoices/schema';
import {
  InvoiceDocumentType,
  documentTypeLabelKey,
  type InvoiceDto,
  type InvoiceLineRequest
} from '@/features/invoices/types';

interface InvoiceFormDialogProps {
  open: boolean;
  /** The draft invoice being edited; `null` opens the create flow. */
  invoice: InvoiceDto | null;
  /**
   * Pre-set document type for a create flow (e.g. a credit/debit note). Ignored in edit mode.
   * Defaults to a purchase invoice.
   */
  presetDocumentType?: InvoiceDocumentType;
  /**
   * When creating a credit/debit note, the original posted invoice this note corrects
   * (SDD-UI-FIN-001 §2.9). Stamped onto the create request as `correctsInvoiceId`.
   */
  correctsInvoiceId?: string | null;
  onClose: () => void;
  /** Called after a successful create/update so the caller can close + refresh. */
  onSaved: () => void;
}

/** The document types selectable in the create form. */
const DOCUMENT_TYPES: InvoiceDocumentType[] = [
  InvoiceDocumentType.PurchaseInvoice,
  InvoiceDocumentType.SaleInvoice,
  InvoiceDocumentType.CreditNote,
  InvoiceDocumentType.DebitNote
];

/** A new blank line in the editor. */
function blankLine(): InvoiceFormValues['lines'][number] {
  return { description: '', quantity: 1, unitPrice: 0, taxRate: 0 };
}

function createDefaults(documentType: InvoiceDocumentType): InvoiceFormValues {
  const today: string = new Date().toISOString().slice(0, 10);
  return {
    documentType,
    counterpartyId: '',
    currencyCode: '',
    issueDate: today,
    dueDate: today,
    lines: [blankLine()]
  };
}

/** Maps a form line to the wire request (the server computes net/tax/gross authoritatively). */
function toLineRequest(line: InvoiceFormValues['lines'][number]): InvoiceLineRequest {
  return {
    description: line.description.trim(),
    quantity: line.quantity,
    unitPrice: line.unitPrice,
    taxRate: line.taxRate
  };
}

/**
 * Create / edit dialog for a DRAFT invoice (SDD-UI-FIN-001 §2.4, §2.5). Header fields (document
 * type, counterparty + currency via {@link useNomenclature}, issue/due dates) plus a dynamic line
 * editor (description, quantity, unit price, tax rate) showing a CLIENT preview of line and header
 * net/tax/gross. The preview is feedback only — the server recomputes the authoritative totals and
 * those persisted values are re-displayed after save. Confirmed/Posted/Cancelled/Reversed invoices
 * are immutable and never reach this dialog (SDD-UI-FIN-001 §2.9). All API failures surface through
 * the mutation hook's `notification.error(getApiErrorMessage(...))`.
 */
export function InvoiceFormDialog({
  open,
  invoice,
  presetDocumentType = InvoiceDocumentType.PurchaseInvoice,
  correctsInvoiceId,
  onClose,
  onSaved
}: InvoiceFormDialogProps) {
  const { t } = useTranslation();
  const { currencies } = useNomenclature();
  const isCompact = useLayoutStore((s) => s.isCompact);
  const { create, update, isSaving } = useInvoiceMutations();
  const isEdit: boolean = invoice !== null;
  const isNote: boolean = Boolean(correctsInvoiceId);

  const {
    control,
    handleSubmit,
    reset,
    formState: { errors }
  } = useForm<InvoiceFormValues>({
    resolver: zodResolver(invoiceFormSchema),
    defaultValues: createDefaults(presetDocumentType),
    mode: 'onChange'
  });

  const { fields, append, remove } = useFieldArray({ control, name: 'lines' });

  useEffect(() => {
    if (!open) {
      return;
    }
    reset(
      invoice
        ? {
            documentType: invoice.documentType,
            counterpartyId: invoice.counterpartyId,
            currencyCode: invoice.currencyCode,
            issueDate: invoice.issueDate.slice(0, 10),
            dueDate: invoice.dueDate.slice(0, 10),
            lines: invoice.lines.map((line) => ({
              description: line.description,
              quantity: line.quantity,
              unitPrice: line.unitPrice,
              taxRate: line.taxRate
            }))
          }
        : createDefaults(presetDocumentType)
    );
  }, [open, invoice, presetDocumentType, reset]);

  const watchedLines = useWatch({ control, name: 'lines' });
  const preview = useMemo(() => previewTotals(watchedLines ?? []), [watchedLines]);

  const persistedTotals = isEdit && invoice ? invoice : null;

  async function onSubmit(values: InvoiceFormValues) {
    const lines: InvoiceLineRequest[] = values.lines.map(toLineRequest);

    if (isEdit && invoice) {
      const result: InvoiceDto | null = await update({
        id: invoice.id,
        request: {
          counterpartyId: values.counterpartyId,
          currencyCode: values.currencyCode,
          issueDate: new Date(values.issueDate).toISOString(),
          dueDate: new Date(values.dueDate).toISOString(),
          lines,
          rowVersion: invoice.rowVersion
        }
      });
      if (result) {
        onSaved();
      }
      return;
    }

    const created: InvoiceDto | null = await create({
      documentType: values.documentType,
      counterpartyId: values.counterpartyId,
      currencyCode: values.currencyCode,
      issueDate: new Date(values.issueDate).toISOString(),
      dueDate: new Date(values.dueDate).toISOString(),
      lines,
      correctsInvoiceId: correctsInvoiceId ?? null
    });
    if (created) {
      onSaved();
    }
  }

  const fieldError = (key?: string): string | undefined => (key ? t(key) : undefined);

  function title(): string {
    if (isEdit) {
      return t('invoices.editTitle');
    }
    if (isNote) {
      return t('invoices.noteTitle');
    }
    return t('invoices.createTitle');
  }

  return (
    <Dialog open={open} onClose={isSaving ? undefined : onClose} maxWidth="lg" fullWidth>
      <DialogContent sx={{ pt: 3 }}>
        <Typography
          component="h2"
          sx={{ fontFamily: serifFamily, fontWeight: 500, fontSize: '1.375rem', mb: 1 }}
        >
          {title()}
        </Typography>
        <Box sx={{ height: '1px', backgroundColor: 'divider', mb: 3 }} />

        <form id="invoice-form" onSubmit={handleSubmit(onSubmit)} noValidate>
          <Stack spacing={2.5}>
            <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
              <Box sx={{ flex: '0 0 220px' }}>
                <Controller
                  name="documentType"
                  control={control}
                  render={({ field }) => (
                    <FormField
                      label={t('invoices.documentType')}
                      required
                      error={fieldError(errors.documentType?.message)}
                    >
                      <AppTextField
                        select
                        value={String(field.value)}
                        disabled={isEdit || isNote}
                        error={Boolean(errors.documentType)}
                        onChange={(e) => field.onChange(Number(e.target.value))}
                      >
                        {DOCUMENT_TYPES.map((dt) => (
                          <MenuItem key={dt} value={String(dt)}>
                            {t(documentTypeLabelKey(dt))}
                          </MenuItem>
                        ))}
                      </AppTextField>
                    </FormField>
                  )}
                />
              </Box>

              <Box sx={{ flex: '1 1 260px', minWidth: 220 }}>
                <Controller
                  name="counterpartyId"
                  control={control}
                  render={({ field }) => (
                    <FormField
                      label={t('invoices.counterparty')}
                      required
                      error={fieldError(errors.counterpartyId?.message)}
                    >
                      <AppTextField
                        {...field}
                        error={Boolean(errors.counterpartyId)}
                        placeholder={t('invoices.counterpartyPlaceholder')}
                      />
                    </FormField>
                  )}
                />
              </Box>

              <Box sx={{ flex: '0 0 130px' }}>
                <Controller
                  name="currencyCode"
                  control={control}
                  render={({ field }) => (
                    <FormField
                      label={t('invoices.currency')}
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

              <Box sx={{ flex: '0 0 170px' }}>
                <Controller
                  name="issueDate"
                  control={control}
                  render={({ field }) => (
                    <FormField
                      label={t('invoices.issueDate')}
                      required
                      error={fieldError(errors.issueDate?.message)}
                    >
                      <AppTextField
                        {...field}
                        type="date"
                        error={Boolean(errors.issueDate)}
                        InputLabelProps={{ shrink: true }}
                      />
                    </FormField>
                  )}
                />
              </Box>

              <Box sx={{ flex: '0 0 170px' }}>
                <Controller
                  name="dueDate"
                  control={control}
                  render={({ field }) => (
                    <FormField
                      label={t('invoices.dueDate')}
                      required
                      error={fieldError(errors.dueDate?.message)}
                    >
                      <AppTextField
                        {...field}
                        type="date"
                        error={Boolean(errors.dueDate)}
                        InputLabelProps={{ shrink: true }}
                      />
                    </FormField>
                  )}
                />
              </Box>
            </Box>

            <Box>
              <Typography variant="overline" component="div" sx={{ mb: 1 }}>
                {t('invoices.lines')}
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
                    <Box sx={{ flex: '2 1 220px', minWidth: 200 }}>
                      <Controller
                        name={`lines.${index}.description`}
                        control={control}
                        render={({ field }) => (
                          <FormField
                            label={t('invoices.lineDescription')}
                            required
                            error={fieldError(errors.lines?.[index]?.description?.message)}
                          >
                            <AppTextField
                              {...field}
                              error={Boolean(errors.lines?.[index]?.description)}
                            />
                          </FormField>
                        )}
                      />
                    </Box>

                    <Box sx={{ flex: '0 0 110px' }}>
                      <Controller
                        name={`lines.${index}.quantity`}
                        control={control}
                        render={({ field }) => (
                          <FormField
                            label={t('invoices.quantity')}
                            required
                            error={fieldError(errors.lines?.[index]?.quantity?.message)}
                          >
                            <AppTextField
                              type="number"
                              value={field.value}
                              error={Boolean(errors.lines?.[index]?.quantity)}
                              onChange={(e) => field.onChange(Number(e.target.value))}
                              inputProps={{ min: 0, step: '0.001' }}
                            />
                          </FormField>
                        )}
                      />
                    </Box>

                    <Box sx={{ flex: '0 0 120px' }}>
                      <Controller
                        name={`lines.${index}.unitPrice`}
                        control={control}
                        render={({ field }) => (
                          <FormField
                            label={t('invoices.unitPrice')}
                            required
                            error={fieldError(errors.lines?.[index]?.unitPrice?.message)}
                          >
                            <AppTextField
                              type="number"
                              value={field.value}
                              error={Boolean(errors.lines?.[index]?.unitPrice)}
                              onChange={(e) => field.onChange(Number(e.target.value))}
                              inputProps={{ min: 0, step: '0.01' }}
                            />
                          </FormField>
                        )}
                      />
                    </Box>

                    <Box sx={{ flex: '0 0 110px' }}>
                      <Controller
                        name={`lines.${index}.taxRate`}
                        control={control}
                        render={({ field }) => (
                          <FormField
                            label={t('invoices.taxRate')}
                            required
                            error={fieldError(errors.lines?.[index]?.taxRate?.message)}
                          >
                            <AppTextField
                              type="number"
                              value={field.value}
                              error={Boolean(errors.lines?.[index]?.taxRate)}
                              onChange={(e) => field.onChange(Number(e.target.value))}
                              inputProps={{ min: 0, step: '0.01' }}
                            />
                          </FormField>
                        )}
                      />
                    </Box>

                    <Box sx={{ flex: '0 0 auto', pt: 3 }}>
                      <IconButton
                        aria-label={t('invoices.removeLine')}
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

              <AppButton
                variant="text"
                startIcon={<AddIcon />}
                onClick={() => append(blankLine())}
                sx={{ mt: 1 }}
              >
                {t('invoices.addLine')}
              </AppButton>
            </Box>

            <Box
              sx={{
                display: 'flex',
                flexDirection: 'column',
                gap: 1,
                borderTop: '1px solid',
                borderColor: 'divider',
                pt: 2
              }}
            >
              <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                {t('invoices.totalsPreviewNote')}
              </Typography>
              <Box sx={{ display: 'flex', gap: 3, flexWrap: 'wrap' }}>
                <Box>
                  <Typography variant="overline" component="div">
                    {t('invoices.netTotal')}
                  </Typography>
                  <MoneyText amount={persistedTotals ? persistedTotals.netTotal : preview.net} />
                </Box>
                <Box>
                  <Typography variant="overline" component="div">
                    {t('invoices.taxTotal')}
                  </Typography>
                  <MoneyText amount={persistedTotals ? persistedTotals.taxTotal : preview.tax} />
                </Box>
                <Box>
                  <Typography variant="overline" component="div">
                    {t('invoices.grossTotal')}
                  </Typography>
                  <MoneyText amount={persistedTotals ? persistedTotals.grossTotal : preview.gross} />
                </Box>
              </Box>
            </Box>
          </Stack>
        </form>
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2 }}>
        <AppButton variant="text" onClick={onClose} disabled={isSaving}>
          {t('common.cancel')}
        </AppButton>
        <AppButton type="submit" form="invoice-form" variant="contained" disabled={isSaving}>
          {isSaving ? t('common.saving') : t('common.save')}
        </AppButton>
      </DialogActions>
    </Dialog>
  );
}
