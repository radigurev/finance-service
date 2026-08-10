import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import type { GridLocaleText } from '@mui/x-data-grid';

/** The four pagination buttons MUI asks us to name for screen readers. */
type PaginationItemType = 'first' | 'last' | 'next' | 'previous';

/**
 * Translated `localeText` for every DataGrid in the app (SDD-UI-001 i18n parity).
 *
 * MUI ships its own English defaults for the grid chrome, so anything NOT overridden here stays
 * English under the BG locale — which is how `0–0 of 0`, `Go to previous page` and `Sort` leaked
 * through. The pagination summary, the pagination button aria-labels, the sort tooltip, the total-rows
 * footer and the no-results label are therefore all bound to `table.*` keys that exist in BOTH locale
 * files. Row selection is disabled app-wide, so its footer text is blanked rather than translated.
 *
 * The columnMenu / filterPanel / toolbar / export strings are deliberately NOT mapped: every grid
 * passes `disableColumnMenu` and none mounts a toolbar or filter panel, so those keys never render.
 *
 * @param noRowsLabel Optional per-grid override for the no-rows line (the caller's empty-state title).
 */
export function useGridLocaleText(noRowsLabel?: string): Partial<GridLocaleText> {
  const { t } = useTranslation();

  return useMemo(() => {
    const localeText: Partial<GridLocaleText> = {
      noResultsOverlayLabel: t('table.noResults'),
      columnHeaderSortIconLabel: t('table.sort'),
      footerTotalRows: t('table.totalRows'),
      footerRowSelected: () => '',
      MuiTablePagination: {
        labelRowsPerPage: t('table.rowsPerPage'),
        labelDisplayedRows: ({ from, to, count }) =>
          count === -1
            ? t('table.displayedRowsUnknownTotal', { from, to })
            : t('table.displayedRows', { from, to, total: count }),
        getItemAriaLabel: (type: PaginationItemType) => t(`table.${type}Page`)
      }
    };

    if (noRowsLabel !== undefined) {
      localeText.noRowsLabel = noRowsLabel;
    }

    return localeText;
  }, [t, noRowsLabel]);
}
