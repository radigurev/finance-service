import { useMemo } from 'react';
import {
  DataGrid,
  type GridColDef,
  type GridPaginationModel,
  type GridSortModel,
  type GridValidRowModel
} from '@mui/x-data-grid';
import { useTranslation } from 'react-i18next';
import { useLayoutStore } from '@/shared/stores/layout';
import { EmptyState } from '@/components/molecules';

interface DataTableProps<TRow extends GridValidRowModel> {
  rows: TRow[];
  columns: GridColDef<TRow>[];
  /** Stable row id accessor. */
  getRowId: (row: TRow) => string | number;
  loading?: boolean;
  /** Total server-side row count for paginated mode. */
  rowCount: number;
  paginationModel: GridPaginationModel;
  onPaginationModelChange: (model: GridPaginationModel) => void;
  sortModel: GridSortModel;
  onSortModelChange: (model: GridSortModel) => void;
  /** Empty-state title shown when there are no rows and not loading. */
  emptyTitle: string;
  /** Optional empty-state description. */
  emptyDescription?: string;
  /** Optional empty-state action node. */
  emptyAction?: React.ReactNode;
}

/** Page-size choices offered in the footer. */
const PAGE_SIZE_OPTIONS = [25, 50, 100, 200];

/**
 * Ledger-styled DataGrid wrapper: server-side paging + sorting, density driven by the
 * layout store, hairline frame, uppercase tracked headers, and an editorial empty state.
 * Column-level mono/right-alignment is opted in by setting a column's
 * `headerClassName`/`cellClassName` to the `ledgerMono` token via {@link ledgerMonoColumn}.
 */
export function DataTable<TRow extends GridValidRowModel>({
  rows,
  columns,
  getRowId,
  loading = false,
  rowCount,
  paginationModel,
  onPaginationModelChange,
  sortModel,
  onSortModelChange,
  emptyTitle,
  emptyDescription,
  emptyAction
}: DataTableProps<TRow>) {
  const { t } = useTranslation();
  const density = useLayoutStore((s) => s.density);

  const localeText = useMemo(
    () => ({
      noRowsLabel: emptyTitle,
      footerRowSelected: () => '',
      MuiTablePagination: {
        labelRowsPerPage: t('table.rowsPerPage')
      }
    }),
    [emptyTitle, t]
  );

  const showEmpty = !loading && rows.length === 0;

  return (
    <DataGrid<TRow>
      autoHeight
      density={density}
      rows={rows}
      columns={columns}
      getRowId={getRowId}
      loading={loading}
      rowCount={rowCount}
      paginationMode="server"
      sortingMode="server"
      pageSizeOptions={PAGE_SIZE_OPTIONS}
      paginationModel={paginationModel}
      onPaginationModelChange={onPaginationModelChange}
      sortModel={sortModel}
      onSortModelChange={onSortModelChange}
      disableColumnMenu
      disableRowSelectionOnClick
      localeText={localeText}
      slots={{
        noRowsOverlay: showEmpty
          ? () => (
              <EmptyState
                framed={false}
                title={emptyTitle}
                description={emptyDescription}
                action={emptyAction}
              />
            )
          : undefined
      }}
      slotProps={{
        loadingOverlay: { variant: 'linear-progress', noRowsVariant: 'skeleton' }
      }}
    />
  );
}

/**
 * Column-spread helper that tags a column as a ledger mono / right-aligned figure
 * (codes, IDs, money). Apply via `{ ...col, ...ledgerMonoColumn }`.
 */
export const ledgerMonoColumn = {
  cellClassName: 'MuiDataGrid-cell--ledgerMono',
  align: 'right' as const,
  headerAlign: 'right' as const
};
