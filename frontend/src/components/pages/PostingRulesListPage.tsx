import { useEffect, useMemo, useState } from 'react';
import { Box } from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import EditIcon from '@mui/icons-material/Edit';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import {
  type GridColDef,
  type GridPaginationModel,
  type GridSortModel
} from '@mui/x-data-grid';
import { useTranslation } from 'react-i18next';
import { ListPageTemplate } from '@/components/templates';
import {
  DataTable,
  ledgerMonoColumn,
  PostingRuleFormDialog,
  ApplyPostingRuleDialog
} from '@/components/organisms';
import { FilterBar } from '@/components/molecules';
import { AppButton, CodeText, StatusDot } from '@/components/atoms';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';
import type { FilterRequest } from '@/shared/api/paging';
import { usePostingRules } from '@/features/postingRules/api';
import type { PostingRuleDto } from '@/features/postingRules/types';

/** Default page size for the posting-rules grid. */
const DEFAULT_PAGE_SIZE = 50;

/**
 * Posting Rules listing (SDD-FIN-006 §2.1). Server-side paging / sorting / search via the
 * GenericFiltering contract (filter by rule key / description / active); create + edit through
 * {@link PostingRuleFormDialog}; apply a rule to an amount context through
 * {@link ApplyPostingRuleDialog}. Rules are reference data, so the list query carries a short
 * staleTime and is invalidated on write. Failures surface via
 * `notification.error(getApiErrorMessage(...))`.
 */
export function PostingRulesListPage() {
  const { t } = useTranslation();

  const [search, setSearch] = useState('');
  const [paginationModel, setPaginationModel] = useState<GridPaginationModel>({
    page: 0,
    pageSize: DEFAULT_PAGE_SIZE
  });
  const [sortModel, setSortModel] = useState<GridSortModel>([{ field: 'ruleKey', sort: 'asc' }]);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editing, setEditing] = useState<PostingRuleDto | null>(null);
  const [applying, setApplying] = useState<PostingRuleDto | null>(null);

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

  const { data, isFetching, error } = usePostingRules(filterRequest);

  useEffect(() => {
    if (error) {
      notification.error(getApiErrorMessage(error, t));
    }
  }, [error, t]);

  function openCreate() {
    setEditing(null);
    setDialogOpen(true);
  }

  function openEdit(rule: PostingRuleDto) {
    setEditing(rule);
    setDialogOpen(true);
  }

  function handleSearchChange(term: string) {
    setSearch(term);
    setPaginationModel((prev) => ({ ...prev, page: 0 }));
  }

  const columns = useMemo<GridColDef<PostingRuleDto>[]>(
    () => [
      {
        field: 'ruleKey',
        headerName: t('postingRules.ruleKey'),
        width: 200,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        renderCell: (params) => <CodeText>{params.row.ruleKey}</CodeText>
      },
      { field: 'description', headerName: t('postingRules.description'), flex: 1, minWidth: 240 },
      {
        field: 'countryCode',
        headerName: t('postingRules.country'),
        width: 110,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) => <CodeText>{params.row.countryCode}</CodeText>
      },
      {
        field: 'lines',
        headerName: t('postingRules.lineCount'),
        width: 110,
        sortable: false,
        ...ledgerMonoColumn,
        renderCell: (params) => <CodeText>{params.row.lines.length}</CodeText>
      },
      {
        field: 'isActive',
        headerName: t('postingRules.active'),
        width: 130,
        sortable: true,
        renderCell: (params) =>
          params.row.isActive ? (
            <StatusDot tone="positive" label={t('postingRules.statusActive')} />
          ) : (
            <StatusDot tone="neutral" label={t('postingRules.statusInactive')} />
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
                aria-label={t('postingRules.apply')}
                onClick={() => setApplying(params.row)}
                sx={{ minWidth: 0, px: 1 }}
              >
                <PlayArrowIcon fontSize="small" />
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
      title={t('postingRules.title')}
      actions={
        <AppButton variant="contained" startIcon={<AddIcon />} onClick={openCreate}>
          {t('postingRules.newRule')}
        </AppButton>
      }
      toolbar={
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap', width: '100%' }}>
          <FilterBar
            value={search}
            onSearchChange={handleSearchChange}
            placeholder={t('postingRules.searchPlaceholder')}
          />
        </Box>
      }
    >
      <DataTable<PostingRuleDto>
        rows={data?.items ?? []}
        columns={columns}
        getRowId={(row) => row.id}
        loading={isFetching}
        rowCount={data?.totalCount ?? 0}
        paginationModel={paginationModel}
        onPaginationModelChange={setPaginationModel}
        sortModel={sortModel}
        onSortModelChange={setSortModel}
        emptyTitle={t('postingRules.empty')}
        emptyDescription={t('postingRules.emptyHint')}
        emptyAction={
          <AppButton variant="outlined" startIcon={<AddIcon />} onClick={openCreate}>
            {t('postingRules.newRule')}
          </AppButton>
        }
      />

      <PostingRuleFormDialog
        open={dialogOpen}
        rule={editing}
        onClose={() => setDialogOpen(false)}
        onSaved={() => setDialogOpen(false)}
      />

      <ApplyPostingRuleDialog rule={applying} onClose={() => setApplying(null)} />
    </ListPageTemplate>
  );
}
