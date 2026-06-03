import { useEffect, useMemo, useState } from 'react';
import { Box } from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import LockIcon from '@mui/icons-material/Lock';
import LockOpenIcon from '@mui/icons-material/LockOpen';
import {
  type GridColDef,
  type GridPaginationModel,
  type GridSortModel
} from '@mui/x-data-grid';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { ListPageTemplate } from '@/components/templates';
import { DataTable, ledgerMonoColumn, GeneratePeriodsDialog } from '@/components/organisms';
import { ReasonPromptDialog, FormField } from '@/components/molecules';
import { AppButton, AppTextField, CodeText, StatusDot } from '@/components/atoms';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';
import type { FilterRequest } from '@/shared/api/paging';
import { searchPeriods } from '@/features/periods/api';
import { usePeriodMutations } from '@/features/periods/usePeriodMutations';
import {
  FiscalPeriodStatus,
  fiscalPeriodStatusLabelKey,
  type FiscalPeriodDto
} from '@/features/periods/types';

/** Default page size for the periods grid. */
const DEFAULT_PAGE_SIZE = 50;

/** Formats an ISO 8601 timestamp as a `yyyy-MM-dd` calendar date for display. */
function formatDate(value: string): string {
  return value.slice(0, 10);
}

/**
 * Fiscal-period listing (SDD-FIN-004). Server-side paging / sorting via the GenericFiltering
 * contract with an optional fiscal-year equals filter; generate a year of periods through
 * {@link GeneratePeriodsDialog}; close / reopen a period through a mandatory-reason prompt
 * ({@link ReasonPromptDialog}). Failures surface via `notification.error(getApiErrorMessage(...))`.
 */
export function FiscalPeriodsListPage() {
  const { t } = useTranslation();
  const { close, reopen, isSaving } = usePeriodMutations();

  const [yearFilter, setYearFilter] = useState('');
  const [paginationModel, setPaginationModel] = useState<GridPaginationModel>({
    page: 0,
    pageSize: DEFAULT_PAGE_SIZE
  });
  const [sortModel, setSortModel] = useState<GridSortModel>([
    { field: 'fiscalYear', sort: 'desc' },
    { field: 'periodNumber', sort: 'asc' }
  ]);
  const [generateOpen, setGenerateOpen] = useState(false);
  const [closing, setClosing] = useState<FiscalPeriodDto | null>(null);
  const [reopening, setReopening] = useState<FiscalPeriodDto | null>(null);

  const filterRequest = useMemo<FilterRequest>(
    () => ({
      page: paginationModel.page + 1,
      pageSize: paginationModel.pageSize,
      filters: yearFilter
        ? [{ field: 'fiscalYear', operator: 'eq', value: Number(yearFilter) }]
        : undefined,
      sort: sortModel
        .filter((s) => s.sort)
        .map((s) => ({ field: s.field, direction: s.sort === 'desc' ? 'desc' : 'asc' }))
    }),
    [paginationModel, sortModel, yearFilter]
  );

  const { data, isFetching, error } = useQuery({
    queryKey: ['periods', filterRequest],
    queryFn: () => searchPeriods(filterRequest),
    placeholderData: (prev) => prev
  });

  useEffect(() => {
    if (error) {
      notification.error(getApiErrorMessage(error, t));
    }
  }, [error, t]);

  function handleYearChange(value: string) {
    setYearFilter(value.replace(/[^0-9]/g, ''));
    setPaginationModel((prev) => ({ ...prev, page: 0 }));
  }

  async function confirmClose(reason: string) {
    if (!closing) {
      return;
    }
    const result = await close({ id: closing.id, reason, rowVersion: closing.rowVersion });
    if (result) {
      setClosing(null);
    }
  }

  async function confirmReopen(reason: string) {
    if (!reopening) {
      return;
    }
    const result = await reopen({ id: reopening.id, reason, rowVersion: reopening.rowVersion });
    if (result) {
      setReopening(null);
    }
  }

  const columns = useMemo<GridColDef<FiscalPeriodDto>[]>(
    () => [
      {
        field: 'fiscalYear',
        headerName: t('periods.fiscalYear'),
        width: 110,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        renderCell: (params) => <CodeText>{params.row.fiscalYear}</CodeText>
      },
      {
        field: 'periodNumber',
        headerName: t('periods.periodNumber'),
        width: 110,
        ...ledgerMonoColumn,
        renderCell: (params) => <CodeText>{params.row.periodNumber}</CodeText>
      },
      { field: 'name', headerName: t('periods.name'), flex: 1, minWidth: 200 },
      {
        field: 'startDate',
        headerName: t('periods.startDate'),
        width: 140,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        sortable: false,
        renderCell: (params) => <CodeText>{formatDate(params.row.startDate)}</CodeText>
      },
      {
        field: 'endDate',
        headerName: t('periods.endDate'),
        width: 140,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        sortable: false,
        renderCell: (params) => <CodeText>{formatDate(params.row.endDate)}</CodeText>
      },
      {
        field: 'status',
        headerName: t('periods.status'),
        width: 130,
        sortable: true,
        renderCell: (params) =>
          params.row.status === FiscalPeriodStatus.Open ? (
            <StatusDot tone="positive" label={t(fiscalPeriodStatusLabelKey(params.row.status))} />
          ) : (
            <StatusDot tone="neutral" label={t(fiscalPeriodStatusLabelKey(params.row.status))} />
          )
      },
      {
        field: 'actions',
        headerName: '',
        width: 64,
        sortable: false,
        align: 'right',
        renderCell: (params) =>
          params.row.status === FiscalPeriodStatus.Open ? (
            <AppButton
              variant="text"
              size="small"
              color="error"
              aria-label={t('periods.close')}
              onClick={() => setClosing(params.row)}
              sx={{ minWidth: 0, px: 1 }}
            >
              <LockIcon fontSize="small" />
            </AppButton>
          ) : (
            <AppButton
              variant="text"
              size="small"
              aria-label={t('periods.reopen')}
              onClick={() => setReopening(params.row)}
              sx={{ minWidth: 0, px: 1 }}
            >
              <LockOpenIcon fontSize="small" />
            </AppButton>
          )
      }
    ],
    [t]
  );

  return (
    <ListPageTemplate
      overline={t('nav.section')}
      title={t('periods.title')}
      actions={
        <AppButton variant="contained" startIcon={<AddIcon />} onClick={() => setGenerateOpen(true)}>
          {t('periods.generateYear')}
        </AppButton>
      }
      toolbar={
        <Box sx={{ display: 'flex', alignItems: 'flex-end', gap: 1.5, flexWrap: 'wrap' }}>
          <Box sx={{ width: 180 }}>
            <FormField label={t('periods.filterByYear')}>
              <AppTextField
                type="number"
                value={yearFilter}
                placeholder={t('periods.allYears')}
                onChange={(e) => handleYearChange(e.target.value)}
                inputProps={{ min: 2000, max: 2100, step: 1 }}
              />
            </FormField>
          </Box>
        </Box>
      }
    >
      <DataTable<FiscalPeriodDto>
        rows={data?.items ?? []}
        columns={columns}
        getRowId={(row) => row.id}
        loading={isFetching}
        rowCount={data?.totalCount ?? 0}
        paginationModel={paginationModel}
        onPaginationModelChange={setPaginationModel}
        sortModel={sortModel}
        onSortModelChange={setSortModel}
        emptyTitle={t('periods.empty')}
        emptyDescription={t('periods.emptyHint')}
        emptyAction={
          <AppButton variant="outlined" startIcon={<AddIcon />} onClick={() => setGenerateOpen(true)}>
            {t('periods.generateYear')}
          </AppButton>
        }
      />

      <GeneratePeriodsDialog
        open={generateOpen}
        onClose={() => setGenerateOpen(false)}
        onGenerated={() => setGenerateOpen(false)}
      />

      <ReasonPromptDialog
        open={closing !== null}
        title={t('periods.closeTitle')}
        message={t('periods.closeMessage', { name: closing?.name ?? '' })}
        confirmLabel={t('periods.close')}
        destructive
        busy={isSaving}
        onConfirm={confirmClose}
        onCancel={() => setClosing(null)}
      />

      <ReasonPromptDialog
        open={reopening !== null}
        title={t('periods.reopenTitle')}
        message={t('periods.reopenMessage', { name: reopening?.name ?? '' })}
        confirmLabel={t('periods.reopen')}
        busy={isSaving}
        onConfirm={confirmReopen}
        onCancel={() => setReopening(null)}
      />
    </ListPageTemplate>
  );
}
