import { Box } from '@mui/material';
import {
  DataGrid,
  type GridColDef,
  type GridPaginationModel,
  type GridSortModel,
  type GridValidRowModel
} from '@mui/x-data-grid';
import { useLayoutStore } from '@/shared/stores/layout';
import { useGridLocaleText } from '@/shared/hooks/useGridLocaleText';
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
 *
 * **The empty state is rendered INSTEAD of the grid, never inside it.** A DataGrid's
 * `noRowsOverlay` lives inside `.MuiDataGrid-virtualScroller`, which is `overflow: hidden` and — in
 * `autoHeight` mode with no rows — only two row-heights tall (`--DataGrid-overlayHeight`). A title +
 * description + action needs roughly twice that, so an overlay-hosted empty state silently clipped its
 * description and hid its action button entirely. Growing the overlay would mean guessing a pixel
 * height that has to hold for both densities and for the longer Bulgarian strings; rendering the state
 * outside any clipping container removes the failure mode instead of tuning it.
 *
 * A grid whose CURRENT PAGE is empty while the server still reports rows (a stale page after a filter
 * narrowed the result set) keeps the grid — and therefore its footer — so the operator can page back;
 * MUI's own single-line overlay carries the title in that case and fits the default height.
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
  const density = useLayoutStore((s) => s.density);
  const localeText = useGridLocaleText(emptyTitle);

  const showEmpty: boolean = !loading && rows.length === 0 && rowCount === 0;

  if (showEmpty) {
    return (
      <Box
        data-testid="data-table-empty"
        sx={{
          border: '1px solid',
          borderColor: 'divider',
          // 4px, matching the DataGrid frame this stands in for, so the surface geometry does not
          // change as the table flips between populated and empty.
          borderRadius: '4px',
          backgroundColor: 'background.paper'
        }}
      >
        <EmptyState
          framed={false}
          title={emptyTitle}
          description={emptyDescription}
          action={emptyAction}
        />
      </Box>
    );
  }

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
