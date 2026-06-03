import { useEffect, useMemo, useState } from 'react';
import { Box } from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import EditIcon from '@mui/icons-material/Edit';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import PublishIcon from '@mui/icons-material/Publish';
import UndoIcon from '@mui/icons-material/Undo';
import {
  type GridColDef,
  type GridPaginationModel,
  type GridSortModel
} from '@mui/x-data-grid';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { ListPageTemplate } from '@/components/templates';
import {
  DataTable,
  ledgerMonoColumn,
  JournalEntryFormDialog,
  ReverseJournalEntryDialog
} from '@/components/organisms';
import { FilterBar, ConfirmDialog } from '@/components/molecules';
import { AppButton, CodeText, StatusDot } from '@/components/atoms';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';
import type { FilterRequest } from '@/shared/api/paging';
import { searchJournalEntries } from '@/features/journal/api';
import { useJournalMutations } from '@/features/journal/useJournalMutations';
import {
  JournalEntryStatus,
  journalStatusLabelKey,
  type JournalEntryDto
} from '@/features/journal/types';

/** Default page size for the journal-entries grid. */
const DEFAULT_PAGE_SIZE = 50;

/** Formats an ISO 8601 timestamp as a `yyyy-MM-dd` calendar date for display. */
function formatDate(value: string): string {
  return value.slice(0, 10);
}

/**
 * Journal-entry listing (SDD-FIN-002 §2.9). Server-side paging / sorting / search via the
 * GenericFiltering contract; create + edit a DRAFT through {@link JournalEntryFormDialog}; post a
 * draft through a confirm dialog; reverse a posted entry through {@link ReverseJournalEntryDialog}.
 * Row actions are gated by status — only drafts edit/delete/post, only posted entries reverse.
 * Failures surface via `notification.error(getApiErrorMessage(...))`.
 */
export function JournalEntriesListPage() {
  const { t } = useTranslation();
  const { remove, post, isSaving } = useJournalMutations();

  const [search, setSearch] = useState('');
  const [paginationModel, setPaginationModel] = useState<GridPaginationModel>({
    page: 0,
    pageSize: DEFAULT_PAGE_SIZE
  });
  const [sortModel, setSortModel] = useState<GridSortModel>([
    { field: 'entryDate', sort: 'desc' }
  ]);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editing, setEditing] = useState<JournalEntryDto | null>(null);
  const [deleting, setDeleting] = useState<JournalEntryDto | null>(null);
  const [posting, setPosting] = useState<JournalEntryDto | null>(null);
  const [reversing, setReversing] = useState<JournalEntryDto | null>(null);

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
    queryKey: ['journal-entries', filterRequest],
    queryFn: () => searchJournalEntries(filterRequest),
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

  function openEdit(entry: JournalEntryDto) {
    setEditing(entry);
    setDialogOpen(true);
  }

  function handleSearchChange(term: string) {
    setSearch(term);
    setPaginationModel((prev) => ({ ...prev, page: 0 }));
  }

  async function confirmDelete() {
    if (!deleting) {
      return;
    }
    const ok = await remove(deleting.id);
    if (ok) {
      setDeleting(null);
    }
  }

  async function confirmPost() {
    if (!posting) {
      return;
    }
    const result = await post({ id: posting.id, rowVersion: posting.rowVersion });
    if (result) {
      setPosting(null);
    }
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

  const columns = useMemo<GridColDef<JournalEntryDto>[]>(
    () => [
      {
        field: 'entryNumber',
        headerName: t('journal.entryNumber'),
        width: 150,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        renderCell: (params) => <CodeText>{params.row.entryNumber ?? '—'}</CodeText>
      },
      {
        field: 'entryDate',
        headerName: t('journal.entryDate'),
        width: 140,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        renderCell: (params) => <CodeText>{formatDate(params.row.entryDate)}</CodeText>
      },
      { field: 'description', headerName: t('journal.description'), flex: 1, minWidth: 220 },
      {
        field: 'baseCurrencyCode',
        headerName: t('journal.baseCurrency'),
        width: 120,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) => <CodeText>{params.row.baseCurrencyCode}</CodeText>
      },
      {
        field: 'status',
        headerName: t('journal.status'),
        width: 140,
        sortable: true,
        renderCell: (params) => (
          <StatusDot
            tone={statusTone(params.row.status)}
            label={t(journalStatusLabelKey(params.row.status))}
          />
        )
      },
      {
        field: 'actions',
        headerName: '',
        width: 168,
        sortable: false,
        align: 'right',
        renderCell: (params) => {
          const isDraft = params.row.status === JournalEntryStatus.Draft;
          const isPosted = params.row.status === JournalEntryStatus.Posted;
          return (
            <Box sx={{ display: 'flex', justifyContent: 'flex-end' }}>
              {isDraft ? (
                <>
                  <AppButton
                    variant="text"
                    size="small"
                    aria-label={t('common.edit')}
                    onClick={() => openEdit(params.row)}
                    sx={{ minWidth: 0, px: 1 }}
                  >
                    <EditIcon fontSize="small" />
                  </AppButton>
                  <AppButton
                    variant="text"
                    size="small"
                    aria-label={t('journal.post')}
                    onClick={() => setPosting(params.row)}
                    sx={{ minWidth: 0, px: 1 }}
                  >
                    <PublishIcon fontSize="small" />
                  </AppButton>
                  <AppButton
                    variant="text"
                    size="small"
                    color="error"
                    aria-label={t('common.delete')}
                    onClick={() => setDeleting(params.row)}
                    sx={{ minWidth: 0, px: 1 }}
                  >
                    <DeleteOutlineIcon fontSize="small" />
                  </AppButton>
                </>
              ) : null}
              {isPosted ? (
                <AppButton
                  variant="text"
                  size="small"
                  color="error"
                  aria-label={t('journal.reverse')}
                  onClick={() => setReversing(params.row)}
                  sx={{ minWidth: 0, px: 1 }}
                >
                  <UndoIcon fontSize="small" />
                </AppButton>
              ) : null}
            </Box>
          );
        }
      }
    ],
    [t]
  );

  return (
    <ListPageTemplate
      overline={t('nav.section')}
      title={t('journal.title')}
      actions={
        <AppButton variant="contained" startIcon={<AddIcon />} onClick={openCreate}>
          {t('journal.newEntry')}
        </AppButton>
      }
      toolbar={
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap', width: '100%' }}>
          <FilterBar
            value={search}
            onSearchChange={handleSearchChange}
            placeholder={t('journal.searchPlaceholder')}
          />
        </Box>
      }
    >
      <DataTable<JournalEntryDto>
        rows={data?.items ?? []}
        columns={columns}
        getRowId={(row) => row.id}
        loading={isFetching}
        rowCount={data?.totalCount ?? 0}
        paginationModel={paginationModel}
        onPaginationModelChange={setPaginationModel}
        sortModel={sortModel}
        onSortModelChange={setSortModel}
        emptyTitle={t('journal.empty')}
        emptyDescription={t('journal.emptyHint')}
        emptyAction={
          <AppButton variant="outlined" startIcon={<AddIcon />} onClick={openCreate}>
            {t('journal.newEntry')}
          </AppButton>
        }
      />

      <JournalEntryFormDialog
        open={dialogOpen}
        entry={editing}
        onClose={() => setDialogOpen(false)}
        onSaved={() => setDialogOpen(false)}
      />

      <ConfirmDialog
        open={posting !== null}
        title={t('journal.postTitle')}
        message={t('journal.postMessage', { description: posting?.description ?? '' })}
        confirmLabel={t('journal.post')}
        busy={isSaving}
        onConfirm={confirmPost}
        onCancel={() => setPosting(null)}
      />

      <ConfirmDialog
        open={deleting !== null}
        title={t('journal.deleteTitle')}
        message={t('journal.deleteMessage', { description: deleting?.description ?? '' })}
        confirmLabel={t('common.delete')}
        destructive
        busy={isSaving}
        onConfirm={confirmDelete}
        onCancel={() => setDeleting(null)}
      />

      <ReverseJournalEntryDialog
        entry={reversing}
        onClose={() => setReversing(null)}
        onReversed={() => setReversing(null)}
      />
    </ListPageTemplate>
  );
}
