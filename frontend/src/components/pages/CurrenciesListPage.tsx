import { useEffect, useMemo, useState } from 'react';
import { Box } from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import EditIcon from '@mui/icons-material/Edit';
import BlockIcon from '@mui/icons-material/Block';
import {
  type GridColDef,
  type GridPaginationModel,
  type GridSortModel
} from '@mui/x-data-grid';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { ListPageTemplate } from '@/components/templates';
import { DataTable, ledgerMonoColumn, CurrencyFormDialog } from '@/components/organisms';
import { FilterBar, ConfirmDialog } from '@/components/molecules';
import { AppButton, CodeText, StatusDot } from '@/components/atoms';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';
import type { FilterRequest } from '@/shared/api/paging';
import { searchCurrencies } from '@/features/currencies/api';
import { useCurrencyMutations } from '@/features/currencies/useCurrencyMutations';
import type { CurrencyDto } from '@/features/currencies/types';

/** Default page size for the currencies grid. */
const DEFAULT_PAGE_SIZE = 50;

/**
 * Currencies listing (SDD-NOM-001 §2.1). Server-side paging / sorting / search by
 * IsoCode or Name via the GenericFiltering contract; create + edit through
 * {@link CurrencyFormDialog}; soft-delete (deactivate) through a confirm dialog. Failures
 * surface via `notification.error(getApiErrorMessage(...))`.
 */
export function CurrenciesListPage() {
  const { t } = useTranslation();
  const { deactivate, isSaving } = useCurrencyMutations();

  const [search, setSearch] = useState('');
  const [paginationModel, setPaginationModel] = useState<GridPaginationModel>({
    page: 0,
    pageSize: DEFAULT_PAGE_SIZE
  });
  const [sortModel, setSortModel] = useState<GridSortModel>([{ field: 'isoCode', sort: 'asc' }]);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editing, setEditing] = useState<CurrencyDto | null>(null);
  const [deactivating, setDeactivating] = useState<CurrencyDto | null>(null);

  const filterRequest = useMemo<FilterRequest>(
    () => ({
      page: paginationModel.page + 1,
      pageSize: paginationModel.pageSize,
      search: search || undefined,
      sort: sortModel
        .filter((s) => s.sort)
        .map((s) => ({ field: s.field, direction: s.sort === 'desc' ? 'desc' : 'asc' }))
    }),
    [paginationModel, sortModel, search]
  );

  const { data, isFetching, error } = useQuery({
    queryKey: ['currencies', filterRequest],
    queryFn: () => searchCurrencies(filterRequest),
    placeholderData: (prev) => prev
  });

  useEffect(() => {
    if (error) {
      notification.error(getApiErrorMessage(error, t));
    }
  }, [error, t]);

  function openCreate() {
    setEditing(null);
    setDialogOpen(true);
  }

  function openEdit(currency: CurrencyDto) {
    setEditing(currency);
    setDialogOpen(true);
  }

  function handleSearchChange(term: string) {
    setSearch(term);
    setPaginationModel((prev) => ({ ...prev, page: 0 }));
  }

  async function confirmDeactivate() {
    if (!deactivating) {
      return;
    }
    const result = await deactivate(deactivating);
    if (result) {
      setDeactivating(null);
    }
  }

  const columns = useMemo<GridColDef<CurrencyDto>[]>(
    () => [
      {
        field: 'isoCode',
        headerName: t('currencies.isoCode'),
        width: 120,
        ...ledgerMonoColumn,
        renderCell: (params) => <CodeText>{params.row.isoCode}</CodeText>
      },
      { field: 'name', headerName: t('currencies.name'), flex: 1, minWidth: 220 },
      {
        field: 'symbol',
        headerName: t('currencies.symbol'),
        width: 110,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) => <CodeText>{params.row.symbol ?? '—'}</CodeText>
      },
      {
        field: 'isActive',
        headerName: t('currencies.active'),
        width: 130,
        sortable: true,
        renderCell: (params) =>
          params.row.isActive ? (
            <StatusDot tone="positive" label={t('currencies.statusActive')} />
          ) : (
            <StatusDot tone="neutral" label={t('currencies.statusInactive')} />
          )
      },
      {
        field: 'actions',
        headerName: '',
        width: 112,
        sortable: false,
        align: 'right',
        renderCell: (params) => (
          <Box sx={{ display: 'flex', justifyContent: 'flex-end' }}>
            <AppButton
              variant="text"
              size="small"
              aria-label={t('common.edit')}
              onClick={() => openEdit(params.row)}
              sx={{ minWidth: 0, px: 1 }}
            >
              <EditIcon fontSize="small" />
            </AppButton>
            {params.row.isActive ? (
              <AppButton
                variant="text"
                size="small"
                color="error"
                aria-label={t('currencies.deactivate')}
                onClick={() => setDeactivating(params.row)}
                sx={{ minWidth: 0, px: 1 }}
              >
                <BlockIcon fontSize="small" />
              </AppButton>
            ) : null}
          </Box>
        )
      }
    ],
    [t]
  );

  return (
    <ListPageTemplate
      overline={t('nav.section')}
      title={t('currencies.title')}
      actions={
        <AppButton variant="contained" startIcon={<AddIcon />} onClick={openCreate}>
          {t('currencies.newCurrency')}
        </AppButton>
      }
      toolbar={
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap', width: '100%' }}>
          <FilterBar
            value={search}
            onSearchChange={handleSearchChange}
            placeholder={t('currencies.searchPlaceholder')}
          />
        </Box>
      }
    >
      <DataTable<CurrencyDto>
        rows={data?.items ?? []}
        columns={columns}
        getRowId={(row) => row.id}
        loading={isFetching}
        rowCount={data?.totalCount ?? 0}
        paginationModel={paginationModel}
        onPaginationModelChange={setPaginationModel}
        sortModel={sortModel}
        onSortModelChange={setSortModel}
        emptyTitle={t('currencies.empty')}
        emptyDescription={t('currencies.emptyHint')}
        emptyAction={
          <AppButton variant="outlined" startIcon={<AddIcon />} onClick={openCreate}>
            {t('currencies.newCurrency')}
          </AppButton>
        }
      />

      <CurrencyFormDialog
        open={dialogOpen}
        currency={editing}
        onClose={() => setDialogOpen(false)}
        onSaved={() => setDialogOpen(false)}
      />

      <ConfirmDialog
        open={deactivating !== null}
        title={t('currencies.deactivateTitle')}
        message={t('currencies.deactivateMessage', { code: deactivating?.isoCode ?? '' })}
        confirmLabel={t('currencies.deactivate')}
        destructive
        busy={isSaving}
        onConfirm={confirmDeactivate}
        onCancel={() => setDeactivating(null)}
      />
    </ListPageTemplate>
  );
}
