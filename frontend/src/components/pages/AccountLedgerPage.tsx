import { useEffect, useMemo, useState } from 'react';
import { Box, Typography } from '@mui/material';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import {
  type GridColDef,
  type GridPaginationModel
} from '@mui/x-data-grid';
import { useTranslation } from 'react-i18next';
import { useParams, useSearchParams } from 'react-router-dom';
import { DataTable, ledgerMonoColumn } from '@/components/organisms';
import { PageHeader } from '@/components/molecules';
import { AppButton, Panel, CodeText, MoneyText, HairlineDivider } from '@/components/atoms';
import { useLayoutStore } from '@/shared/stores/layout';
import { useGoBack } from '@/shared/hooks/useGoBack';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';
import { MAX_PAGE_SIZE } from '@/shared/api/paging';
import { useAccountLedger } from '@/features/generalLedger/api';
import type { AccountLedgerLineDto } from '@/features/generalLedger/types';
import { serifFamily } from '@/shared/theme';

/** Default page size for the ledger lines grid (within the 200 cap, SDD-INFRA-005). */
const DEFAULT_PAGE_SIZE = 50;

/** Formats an ISO 8601 timestamp as a `yyyy-MM-dd` calendar date for display. */
function formatDate(value: string): string {
  return value.slice(0, 10);
}

/**
 * Account Ledger (SDD-FIN-003 §2.3) — the drill-down behind a trial-balance row. Reads the
 * route `accountId` and an optional `fromDate`/`toDate` window from search params, then renders
 * the account header (code/name + Back button), the opening balance, a paged table of posted
 * lines (entry number, date, description, debit, credit, running balance — money in the tabular
 * mono face), and the closing balance. An account with no posted activity yields a well-formed
 * empty ledger with zero balances (not an error). Transactional read: never cached.
 * Back navigation uses the shared {@link useGoBack}; failures surface via `notification.error`.
 */
export function AccountLedgerPage() {
  const { t } = useTranslation();
  const { accountId: accountIdParam } = useParams<{ accountId: string }>();
  const [searchParams] = useSearchParams();
  const isCompact = useLayoutStore((s) => s.isCompact);
  const { goBack } = useGoBack({ fallback: { pathname: '/general-ledger' } });

  const accountId = Number(accountIdParam);
  const fromDate = searchParams.get('fromDate') ?? undefined;
  const toDate = searchParams.get('toDate') ?? undefined;

  const [paginationModel, setPaginationModel] = useState<GridPaginationModel>({
    page: 0,
    pageSize: DEFAULT_PAGE_SIZE
  });

  const { data, isFetching, error } = useAccountLedger(accountId, {
    fromDate,
    toDate,
    page: paginationModel.page + 1,
    pageSize: Math.min(paginationModel.pageSize, MAX_PAGE_SIZE)
  });

  useEffect(() => {
    if (error) {
      notification.error(getApiErrorMessage(error, t));
    }
  }, [error, t]);

  const columns = useMemo<GridColDef<AccountLedgerLineDto>[]>(
    () => [
      {
        field: 'entryNumber',
        headerName: t('generalLedger.entryNumber'),
        width: 160,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        sortable: false,
        renderCell: (params) => <CodeText>{params.row.entryNumber}</CodeText>
      },
      {
        field: 'entryDate',
        headerName: t('generalLedger.date'),
        width: 140,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        sortable: false,
        renderCell: (params) => <CodeText>{formatDate(params.row.entryDate)}</CodeText>
      },
      {
        field: 'description',
        headerName: t('generalLedger.description'),
        flex: 1,
        minWidth: 220,
        sortable: false,
        renderCell: (params) => params.row.description ?? '—'
      },
      {
        field: 'debit',
        headerName: t('generalLedger.debit'),
        width: 150,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) =>
          params.row.debit !== 0 ? <MoneyText amount={params.row.debit} /> : <span>—</span>
      },
      {
        field: 'credit',
        headerName: t('generalLedger.credit'),
        width: 150,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) =>
          params.row.credit !== 0 ? <MoneyText amount={params.row.credit} /> : <span>—</span>
      },
      {
        field: 'runningBalance',
        headerName: t('generalLedger.runningBalance'),
        width: 170,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) => <MoneyText amount={params.row.runningBalance} />
      }
    ],
    [t]
  );

  const accountLabel = data
    ? `${data.accountCode ?? `#${data.accountId}`}${data.accountName ? ` · ${data.accountName}` : ''}`
    : `#${accountId}`;

  return (
    <Box>
      <PageHeader
        overline={t('generalLedger.generalLedgerTitle')}
        title={t('generalLedger.accountLedgerTitle')}
        subtitle={accountLabel}
        actions={
          <AppButton variant="outlined" startIcon={<ArrowBackIcon />} onClick={goBack}>
            {t('common.back')}
          </AppButton>
        }
      />

      <Panel sx={{ mb: 3 }}>
        <Box
          sx={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 2,
            flexWrap: 'wrap'
          }}
        >
          <Box>
            <Typography variant="overline" sx={{ display: 'block' }}>
              {t('generalLedger.openingBalance')}
            </Typography>
            <MoneyText
              amount={data?.openingBalance ?? 0}
              sx={{ fontFamily: serifFamily, fontSize: '1.5rem', lineHeight: 1.1 }}
            />
          </Box>
          <Box sx={{ textAlign: 'right' }}>
            <Typography variant="overline" sx={{ display: 'block' }}>
              {t('generalLedger.closingBalance')}
            </Typography>
            <MoneyText
              amount={data?.closingBalance ?? 0}
              sx={{ fontFamily: serifFamily, fontSize: '1.5rem', lineHeight: 1.1 }}
            />
          </Box>
        </Box>
      </Panel>

      <DataTable<AccountLedgerLineDto>
        rows={data?.lines.items ?? []}
        columns={columns}
        getRowId={(row) => row.lineId}
        loading={isFetching}
        rowCount={data?.lines.totalCount ?? 0}
        paginationModel={paginationModel}
        onPaginationModelChange={setPaginationModel}
        sortModel={[]}
        onSortModelChange={() => undefined}
        emptyTitle={t('generalLedger.ledgerEmpty')}
        emptyDescription={t('generalLedger.ledgerEmptyHint')}
      />

      <HairlineDivider sx={{ mt: 3, mb: isCompact ? 1.5 : 2 }} />
    </Box>
  );
}
