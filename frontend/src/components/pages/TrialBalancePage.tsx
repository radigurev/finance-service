import { useEffect, useMemo, useState } from 'react';
import { Box } from '@mui/material';
import { DataGrid, type GridColDef } from '@mui/x-data-grid';
import { useTranslation } from 'react-i18next';
import { useNavigate, createSearchParams } from 'react-router-dom';
import { ListPageTemplate } from '@/components/templates';
import { ledgerMonoColumn } from '@/components/organisms';
import { FormField, EmptyState } from '@/components/molecules';
import { AppTextField, Panel, CodeText, MoneyText, StatusDot, HairlineDivider } from '@/components/atoms';
import { useLayoutStore } from '@/shared/stores/layout';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';
import { useTrialBalance } from '@/features/generalLedger/api';
import type { TrialBalanceRowDto } from '@/features/generalLedger/types';

/** Returns today's date as a `yyyy-MM-dd` string for the default as-of value. */
function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}

/**
 * Trial Balance (SDD-FIN-003 §2.2). The user picks a required as-of date and an optional from
 * date; the table lists every account with posted activity in the window — code (mono) + name,
 * its debit balance and credit balance — closed by a grand-total footer and a Balanced indicator
 * (green when `balanced`, oxblood otherwise). Clicking a row drills into that account's ledger,
 * carrying the current date window via search params. Transactional read: never cached.
 * Failures surface via `notification.error(getApiErrorMessage(...))`.
 */
export function TrialBalancePage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const isCompact = useLayoutStore((s) => s.isCompact);
  const density = useLayoutStore((s) => s.density);

  const [asOfDate, setAsOfDate] = useState(todayIso());
  const [fromDate, setFromDate] = useState('');

  const rangeInvalid = fromDate !== '' && asOfDate !== '' && fromDate > asOfDate;

  const { data, isFetching, error } = useTrialBalance(
    asOfDate,
    fromDate !== '' && !rangeInvalid ? fromDate : undefined
  );

  useEffect(() => {
    if (error) {
      notification.error(getApiErrorMessage(error, t));
    }
  }, [error, t]);

  function openAccountLedger(row: TrialBalanceRowDto) {
    const params: Record<string, string> = {};
    if (fromDate !== '' && !rangeInvalid) {
      params.fromDate = fromDate;
    }
    if (asOfDate !== '') {
      params.toDate = asOfDate;
    }
    const search = createSearchParams(params).toString();
    navigate({
      pathname: `/general-ledger/accounts/${row.accountId}`,
      search: search ? `?${search}` : ''
    });
  }

  const columns = useMemo<GridColDef<TrialBalanceRowDto>[]>(
    () => [
      {
        field: 'accountCode',
        headerName: t('generalLedger.account'),
        flex: 1,
        minWidth: 260,
        sortable: false,
        renderCell: (params) => (
          <Box sx={{ display: 'flex', alignItems: 'baseline', gap: 1, minWidth: 0 }}>
            <CodeText>{params.row.accountCode ?? `#${params.row.accountId}`}</CodeText>
            <Box
              component="span"
              sx={{ color: 'text.secondary', overflow: 'hidden', textOverflow: 'ellipsis' }}
            >
              {params.row.accountName ?? ''}
            </Box>
          </Box>
        )
      },
      {
        field: 'debitBalance',
        headerName: t('generalLedger.debit'),
        width: 180,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) =>
          params.row.debitBalance !== 0 ? <MoneyText amount={params.row.debitBalance} /> : <span>—</span>
      },
      {
        field: 'creditBalance',
        headerName: t('generalLedger.credit'),
        width: 180,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) =>
          params.row.creditBalance !== 0 ? <MoneyText amount={params.row.creditBalance} /> : <span>—</span>
      }
    ],
    [t]
  );

  const rows = data?.rows ?? [];
  const showEmpty = !isFetching && rows.length === 0;

  return (
    <ListPageTemplate
      overline={t('nav.section')}
      title={t('generalLedger.trialBalanceTitle')}
      toolbar={
        <Box sx={{ display: 'flex', alignItems: 'flex-end', gap: 2, flexWrap: 'wrap' }}>
          <Box sx={{ width: 200 }}>
            <FormField label={t('generalLedger.asOfDate')} required>
              <AppTextField
                type="date"
                value={asOfDate}
                onChange={(e) => setAsOfDate(e.target.value)}
                InputLabelProps={{ shrink: true }}
              />
            </FormField>
          </Box>
          <Box sx={{ width: 200 }}>
            <FormField
              label={t('generalLedger.fromDateOptional')}
              error={rangeInvalid ? t('generalLedger.invalidRange') : undefined}
            >
              <AppTextField
                type="date"
                value={fromDate}
                error={rangeInvalid}
                onChange={(e) => setFromDate(e.target.value)}
                InputLabelProps={{ shrink: true }}
              />
            </FormField>
          </Box>
        </Box>
      }
    >
      <Panel flush>
        <DataGrid<TrialBalanceRowDto>
          autoHeight
          density={density}
          rows={rows}
          columns={columns}
          getRowId={(row) => row.accountId}
          loading={isFetching}
          onRowClick={(params) => openAccountLedger(params.row)}
          hideFooter
          disableColumnMenu
          disableRowSelectionOnClick
          sx={{ border: 'none', '& .MuiDataGrid-row': { cursor: 'pointer' } }}
          slots={{
            noRowsOverlay: showEmpty
              ? () => (
                  <EmptyState
                    framed={false}
                    title={t('generalLedger.tbEmpty')}
                    description={t('generalLedger.tbEmptyHint')}
                  />
                )
              : undefined
          }}
          slotProps={{
            loadingOverlay: { variant: 'linear-progress', noRowsVariant: 'skeleton' }
          }}
        />

        {rows.length > 0 ? (
          <Box>
            <HairlineDivider />
            <Box
              sx={{
                display: 'flex',
                alignItems: 'center',
                gap: 2,
                px: isCompact ? 2 : 3,
                py: isCompact ? 1.5 : 2
              }}
            >
              <Box
                component="span"
                sx={{ flex: 1, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.06em', fontSize: '0.8125rem' }}
              >
                {t('generalLedger.grandTotal')}
              </Box>
              <Box sx={{ width: 180, textAlign: 'right' }}>
                <MoneyText amount={data?.grandTotalDebit ?? 0} sx={{ fontWeight: 600 }} />
              </Box>
              <Box sx={{ width: 180, textAlign: 'right' }}>
                <MoneyText amount={data?.grandTotalCredit ?? 0} sx={{ fontWeight: 600 }} />
              </Box>
            </Box>
            <HairlineDivider />
            <Box sx={{ px: isCompact ? 2 : 3, py: isCompact ? 1.5 : 2 }}>
              <StatusDot
                tone={data?.balanced ? 'positive' : 'danger'}
                label={data?.balanced ? t('generalLedger.balanced') : t('generalLedger.unbalanced')}
              />
            </Box>
          </Box>
        ) : null}
      </Panel>
    </ListPageTemplate>
  );
}
