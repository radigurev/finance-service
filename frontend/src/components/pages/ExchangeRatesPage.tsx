import { useEffect, useMemo, useState } from 'react';
import { Box, MenuItem, Stack, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material';
import {
  DataGrid,
  type GridColDef
} from '@mui/x-data-grid';
import { useTranslation } from 'react-i18next';
import { ListPageTemplate } from '@/components/templates';
import { ledgerMonoColumn } from '@/components/organisms';
import { FormField, EmptyState } from '@/components/molecules';
import { AppTextField, Panel, CodeText, SectionHeading, MoneyText } from '@/components/atoms';
import { useNomenclature } from '@/shared/hooks/useNomenclature';
import { useLayoutStore } from '@/shared/stores/layout';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';
import { useLatestRate, useRateRange } from '@/features/currencies/useExchangeRates';
import type { ExchangeRateDto } from '@/features/currencies/types';

type Mode = 'latest' | 'range';

/** Formats an ISO 8601 timestamp as a `yyyy-MM-dd` calendar date for display. */
function formatRateDate(value: string): string {
  return value.slice(0, 10);
}

/** Renders the six-decimal rate in the tabular mono face, right-aligned. */
function rateColumn(label: string): GridColDef<ExchangeRateDto> {
  return {
    field: 'rate',
    headerName: label,
    flex: 1,
    minWidth: 160,
    ...ledgerMonoColumn,
    sortable: false,
    renderCell: (params) => <MoneyText amount={params.row.rate} fractionDigits={6} />
  };
}

/**
 * Read-only exchange-rate explorer (SDD-NOM-001 §2.2). The user picks a currency and either
 * a single date (latest rate on or before) or a date range (table of rates ordered by date).
 * Rates render in the tabular mono face, right-aligned. Exchange-rate WRITE is out of scope.
 * Failures surface via `notification.error(getApiErrorMessage(...))`.
 */
export function ExchangeRatesPage() {
  const { t } = useTranslation();
  const { currencies, isLoading: currenciesLoading } = useNomenclature();
  const density = useLayoutStore((s) => s.density);

  const [mode, setMode] = useState<Mode>('latest');
  const [currency, setCurrency] = useState('');
  const [date, setDate] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');

  const rangeValid = from !== '' && to !== '' && from <= to;

  const latestQuery = useLatestRate({ currency, date, enabled: mode === 'latest' });
  const rangeQuery = useRateRange({ currency, from, to, enabled: mode === 'range' && rangeValid });

  useEffect(() => {
    if (mode === 'latest' && latestQuery.error) {
      notification.error(getApiErrorMessage(latestQuery.error, t));
    }
  }, [mode, latestQuery.error, t]);

  useEffect(() => {
    if (mode === 'range' && rangeQuery.error) {
      notification.error(getApiErrorMessage(rangeQuery.error, t));
    }
  }, [mode, rangeQuery.error, t]);

  function handleModeChange(_event: React.MouseEvent<HTMLElement>, next: Mode | null) {
    if (next) {
      setMode(next);
    }
  }

  const columns = useMemo<GridColDef<ExchangeRateDto>[]>(
    () => [
      {
        field: 'rateDate',
        headerName: t('exchangeRates.date'),
        width: 160,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        sortable: false,
        renderCell: (params) => <CodeText>{formatRateDate(params.row.rateDate)}</CodeText>
      },
      rateColumn(t('exchangeRates.rate'))
    ],
    [t]
  );

  const rangeRows = rangeQuery.data ?? [];
  const latest = latestQuery.data;
  const dateRangeInvalid = mode === 'range' && from !== '' && to !== '' && from > to;

  return (
    <ListPageTemplate overline={t('nav.section')} title={t('exchangeRates.title')}>
      <Panel sx={{ mb: 3 }}>
        <SectionHeading overline={t('exchangeRates.queryOverline')}>
          {t('exchangeRates.queryHeading')}
        </SectionHeading>

        <Stack spacing={2.5}>
          <ToggleButtonGroup
            value={mode}
            exclusive
            onChange={handleModeChange}
            size={density === 'compact' ? 'small' : 'medium'}
          >
            <ToggleButton value="latest">{t('exchangeRates.modeLatest')}</ToggleButton>
            <ToggleButton value="range">{t('exchangeRates.modeRange')}</ToggleButton>
          </ToggleButtonGroup>

          <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
            <Box sx={{ flex: '1 1 220px', minWidth: 200 }}>
              <FormField label={t('exchangeRates.currency')} required>
                <AppTextField
                  select
                  value={currency}
                  disabled={currenciesLoading}
                  onChange={(e) => setCurrency(e.target.value)}
                >
                  {currencies.map((c) => (
                    <MenuItem key={c.code} value={c.code}>
                      <CodeText sx={{ mr: 1 }}>{c.code}</CodeText>
                      {c.name}
                    </MenuItem>
                  ))}
                </AppTextField>
              </FormField>
            </Box>

            {mode === 'latest' ? (
              <Box sx={{ flex: '1 1 220px', minWidth: 200 }}>
                <FormField label={t('exchangeRates.asOfDate')} required>
                  <AppTextField
                    type="date"
                    value={date}
                    onChange={(e) => setDate(e.target.value)}
                    InputLabelProps={{ shrink: true }}
                  />
                </FormField>
              </Box>
            ) : (
              <>
                <Box sx={{ flex: '1 1 200px', minWidth: 180 }}>
                  <FormField label={t('exchangeRates.from')} required>
                    <AppTextField
                      type="date"
                      value={from}
                      onChange={(e) => setFrom(e.target.value)}
                      InputLabelProps={{ shrink: true }}
                    />
                  </FormField>
                </Box>
                <Box sx={{ flex: '1 1 200px', minWidth: 180 }}>
                  <FormField
                    label={t('exchangeRates.to')}
                    required
                    error={dateRangeInvalid ? t('exchangeRates.invalidRange') : undefined}
                  >
                    <AppTextField
                      type="date"
                      value={to}
                      error={dateRangeInvalid}
                      onChange={(e) => setTo(e.target.value)}
                      InputLabelProps={{ shrink: true }}
                    />
                  </FormField>
                </Box>
              </>
            )}
          </Box>
        </Stack>
      </Panel>

      {mode === 'latest' ? (
        <Panel>
          <SectionHeading overline={t('exchangeRates.resultOverline')}>
            {t('exchangeRates.latestHeading')}
          </SectionHeading>
          {latest ? (
            <Box sx={{ display: 'flex', alignItems: 'baseline', gap: 2, flexWrap: 'wrap' }}>
              <MoneyText
                amount={latest.rate}
                fractionDigits={6}
                sx={{ fontSize: '2rem', lineHeight: 1.1 }}
              />
              <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                {t('exchangeRates.asOf', { date: formatRateDate(latest.rateDate) })}
                {' · '}
                <CodeText>{latest.currencyIsoCode}</CodeText>
              </Typography>
            </Box>
          ) : (
            <EmptyState
              framed={false}
              title={t('exchangeRates.noSelectionTitle')}
              description={t('exchangeRates.noSelectionLatestHint')}
            />
          )}
        </Panel>
      ) : (
        <DataGrid<ExchangeRateDto>
          autoHeight
          density={density}
          rows={rangeRows}
          columns={columns}
          getRowId={(row) => row.rateDate}
          loading={rangeQuery.isFetching}
          hideFooter
          disableColumnMenu
          disableRowSelectionOnClick
          slots={{
            noRowsOverlay: () => (
              <EmptyState
                framed={false}
                title={t('exchangeRates.noSelectionTitle')}
                description={t('exchangeRates.noSelectionRangeHint')}
              />
            )
          }}
        />
      )}
    </ListPageTemplate>
  );
}
