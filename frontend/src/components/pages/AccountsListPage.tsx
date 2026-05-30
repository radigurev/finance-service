import { useEffect, useMemo, useState } from 'react';
import { Box } from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import EditIcon from '@mui/icons-material/Edit';
import {
  type GridColDef,
  type GridPaginationModel,
  type GridSortModel
} from '@mui/x-data-grid';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { ListPageTemplate } from '@/components/templates';
import { DataTable, ledgerMonoColumn, AccountFormDialog } from '@/components/organisms';
import { FilterBar } from '@/components/molecules';
import { AppButton, CodeText, StatusDot } from '@/components/atoms';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';
import type { FilterRequest } from '@/shared/api/paging';
import { searchAccounts } from '@/features/accounts/api';
import { accountTypeLabelKey, type AccountDto } from '@/features/accounts/types';

/** Default page size for the accounts grid. */
const DEFAULT_PAGE_SIZE = 50;

/**
 * Chart-of-accounts listing. Server-side paging / sorting / search via the
 * GenericFiltering contract; create + edit through {@link AccountFormDialog}. Failures
 * surface via `notification.error(getApiErrorMessage(...))`.
 */
export function AccountsListPage() {
  const { t } = useTranslation();

  const [search, setSearch] = useState('');
  const [paginationModel, setPaginationModel] = useState<GridPaginationModel>({
    page: 0,
    pageSize: DEFAULT_PAGE_SIZE
  });
  const [sortModel, setSortModel] = useState<GridSortModel>([{ field: 'code', sort: 'asc' }]);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editing, setEditing] = useState<AccountDto | null>(null);

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
    queryKey: ['accounts', filterRequest],
    queryFn: () => searchAccounts(filterRequest),
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

  function openEdit(account: AccountDto) {
    setEditing(account);
    setDialogOpen(true);
  }

  function handleSearchChange(term: string) {
    setSearch(term);
    setPaginationModel((prev) => ({ ...prev, page: 0 }));
  }

  const columns = useMemo<GridColDef<AccountDto>[]>(
    () => [
      {
        field: 'code',
        headerName: t('accounts.code'),
        width: 140,
        ...ledgerMonoColumn,
        renderCell: (params) => <CodeText>{params.row.code}</CodeText>
      },
      { field: 'name', headerName: t('accounts.name'), flex: 1, minWidth: 220 },
      {
        field: 'type',
        headerName: t('accounts.type'),
        width: 160,
        sortable: true,
        valueGetter: (_value, row) => t(accountTypeLabelKey(row.type))
      },
      {
        field: 'countryCode',
        headerName: t('accounts.country'),
        width: 110,
        ...ledgerMonoColumn,
        renderCell: (params) => <CodeText>{params.row.countryCode}</CodeText>
      },
      {
        field: 'isActive',
        headerName: t('accounts.active'),
        width: 130,
        sortable: true,
        renderCell: (params) =>
          params.row.isActive ? (
            <StatusDot tone="positive" label={t('accounts.statusActive')} />
          ) : (
            <StatusDot tone="neutral" label={t('accounts.statusInactive')} />
          )
      },
      {
        field: 'actions',
        headerName: '',
        width: 64,
        sortable: false,
        align: 'right',
        renderCell: (params) => (
          <AppButton
            variant="text"
            size="small"
            aria-label={t('common.edit')}
            onClick={() => openEdit(params.row)}
            sx={{ minWidth: 0, px: 1 }}
          >
            <EditIcon fontSize="small" />
          </AppButton>
        )
      }
    ],
    [t]
  );

  return (
    <ListPageTemplate
      overline={t('nav.section')}
      title={t('accounts.title')}
      actions={
        <AppButton variant="contained" startIcon={<AddIcon />} onClick={openCreate}>
          {t('accounts.newAccount')}
        </AppButton>
      }
      toolbar={
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap', width: '100%' }}>
          <FilterBar value={search} onSearchChange={handleSearchChange} />
        </Box>
      }
    >
      <DataTable<AccountDto>
        rows={data?.items ?? []}
        columns={columns}
        getRowId={(row) => row.id}
        loading={isFetching}
        rowCount={data?.totalCount ?? 0}
        paginationModel={paginationModel}
        onPaginationModelChange={setPaginationModel}
        sortModel={sortModel}
        onSortModelChange={setSortModel}
        emptyTitle={t('accounts.empty')}
        emptyDescription={t('accounts.emptyHint')}
        emptyAction={
          <AppButton variant="outlined" startIcon={<AddIcon />} onClick={openCreate}>
            {t('accounts.newAccount')}
          </AppButton>
        }
      />

      <AccountFormDialog
        open={dialogOpen}
        account={editing}
        onClose={() => setDialogOpen(false)}
        onSaved={() => setDialogOpen(false)}
      />
    </ListPageTemplate>
  );
}
