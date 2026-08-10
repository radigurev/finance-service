import { describe, it, expect, afterEach } from 'vitest';
import { screen, within } from '@testing-library/react';
import type { GridColDef } from '@mui/x-data-grid';
import { renderWithProviders } from '@/test/renderWithProviders';
import { useLayoutStore } from '@/shared/stores/layout';
import i18n from '@/shared/i18n/i18n';
import { AppButton } from '@/components/atoms';
import { DataTable } from './DataTable';

interface Row {
  id: string;
  code: string;
}

const columns: GridColDef<Row>[] = [
  { field: 'code', headerName: 'Code', width: 120, sortable: true }
];

/** A long, sentence-shaped description — the kind that was being clipped mid-sentence. */
const DESCRIPTION =
  'Record the first receipt or supplier payment to start the cash ledger, then allocate it against an open invoice.';

function renderTable(over: Partial<Parameters<typeof DataTable<Row>>[0]> = {}) {
  return renderWithProviders(
    <DataTable<Row>
      rows={[]}
      columns={columns}
      getRowId={(row) => row.id}
      rowCount={0}
      paginationModel={{ page: 0, pageSize: 25 }}
      onPaginationModelChange={() => undefined}
      sortModel={[]}
      onSortModelChange={() => undefined}
      emptyTitle="No payments yet."
      emptyDescription={DESCRIPTION}
      emptyAction={<AppButton variant="outlined">New payment</AppButton>}
      {...over}
    />
  );
}

/**
 * Walks up from `element` looking for the containers that clipped the empty state in the browser: the
 * DataGrid's virtual scroller (`overflow-y: hidden`, and only two row-heights tall while empty) and the
 * overlay wrapper MUI renders inside it at a JS-computed fixed height.
 */
function clippingAncestors(element: HTMLElement): string[] {
  const offenders: string[] = [];
  let node: HTMLElement | null = element.parentElement;

  while (node && node !== document.body) {
    if (
      node.classList.contains('MuiDataGrid-virtualScroller') ||
      node.classList.contains('MuiDataGrid-overlayWrapper') ||
      node.classList.contains('MuiDataGrid-overlayWrapperInner')
    ) {
      offenders.push(node.className);
    }
    node = node.parentElement;
  }

  return offenders;
}

describe('DataTable empty state (SDD-UI-FIN-002 §2.1 / ui-validate D3+D4)', () => {
  afterEach(async () => {
    useLayoutStore.setState({ isCompact: false, density: 'standard' });
    await i18n.changeLanguage('en');
  });

  it('DataTable_Empty_ActionButtonIsRenderedOutsideEveryGridClippingContainer', async () => {
    renderTable();

    // The action existed before this fix too — it was simply painted below the scroller's clip, so its
    // presence alone is not enough: it must sit outside every container that can hide it.
    const action = await screen.findByRole('button', { name: 'New payment' });
    expect(action).toBeVisible();
    expect(clippingAncestors(action)).toEqual([]);
    expect(document.querySelector('.MuiDataGrid-virtualScroller')).toBeNull();
  });

  it('DataTable_Empty_RendersTheWholeDescription_NotAClippedPrefix', async () => {
    renderTable();

    const description = await screen.findByText(DESCRIPTION);
    expect(description).toBeVisible();
    expect(clippingAncestors(description)).toEqual([]);
    // The title, description and action are one editorial block, so all three share a frame.
    const frame = screen.getByTestId('data-table-empty');
    expect(within(frame).getByText('No payments yet.')).toBeInTheDocument();
    expect(within(frame).getByRole('button', { name: 'New payment' })).toBeInTheDocument();
  });

  it('DataTable_Empty_HoldsAtCompactDensity', async () => {
    // A compact grid's overlay is SHORTER (the height is a multiple of the row height), so compact was
    // the worse of the two densities. Nothing about the fix may depend on density.
    useLayoutStore.setState({ isCompact: true, density: 'compact' });

    renderTable();

    const action = await screen.findByRole('button', { name: 'New payment' });
    expect(clippingAncestors(action)).toEqual([]);
    expect(await screen.findByText(DESCRIPTION)).toBeVisible();
  });

  it('DataTable_Empty_HoldsForTheLongerBulgarianCopy', async () => {
    await i18n.changeLanguage('bg');
    const bgTitle = 'Няма регистрирани плащания.';
    const bgDescription =
      'Регистрирайте първото постъпление или плащане към доставчик, за да започнете касовия дневник.';

    renderTable({
      emptyTitle: bgTitle,
      emptyDescription: bgDescription,
      emptyAction: <AppButton variant="outlined">Ново плащане</AppButton>
    });

    const action = await screen.findByRole('button', { name: 'Ново плащане' });
    expect(clippingAncestors(action)).toEqual([]);
    expect(await screen.findByText(bgDescription)).toBeVisible();
    expect(await screen.findByText(bgTitle)).toBeVisible();
  });

  it('DataTable_EmptyPageButServerHasRows_KeepsTheGridSoTheOperatorCanPageBack', async () => {
    // Rows can be empty while the server still reports a total — a stale page after a filter narrowed
    // the result set. Replacing the grid there would strand the operator with no pager.
    renderTable({ rowCount: 120, paginationModel: { page: 4, pageSize: 25 } });

    expect(await screen.findByRole('columnheader', { name: 'Code' })).toBeInTheDocument();
    expect(document.querySelector('.MuiDataGrid-footerContainer')).not.toBeNull();
    expect(screen.queryByTestId('data-table-empty')).toBeNull();
  });
});

describe('DataTable grid chrome i18n (ui-validate D6)', () => {
  afterEach(async () => {
    await i18n.changeLanguage('en');
  });

  const rows: Row[] = [
    { id: '1', code: 'A' },
    { id: '2', code: 'B' }
  ];

  function renderPopulated() {
    return renderWithProviders(
      <DataTable<Row>
        rows={rows}
        columns={columns}
        getRowId={(row) => row.id}
        rowCount={2}
        paginationModel={{ page: 0, pageSize: 25 }}
        onPaginationModelChange={() => undefined}
        sortModel={[]}
        onSortModelChange={() => undefined}
        emptyTitle="No rows."
      />
    );
  }

  it('DataTable_EnLocale_KeepsTheEnglishPaginationChrome', async () => {
    renderPopulated();

    await screen.findByRole('columnheader', { name: 'Code' });
    expect(document.querySelector('.MuiTablePagination-displayedRows')?.textContent).toBe('1–2 of 2');
    expect(screen.getByText('Rows per page')).toBeInTheDocument();
    expect(screen.getByLabelText('Go to next page')).toBeInTheDocument();
  });

  it('DataTable_BgLocale_TranslatesPaginationSummaryRowsPerPageAndPagerLabels', async () => {
    // Before the fix the grid still read `0–0 of 0` / `Go to previous page` / `Sort` in Bulgarian:
    // MUI ships English defaults for everything `localeText` does not override.
    await i18n.changeLanguage('bg');

    renderPopulated();

    await screen.findByRole('columnheader', { name: 'Code' });
    expect(document.querySelector('.MuiTablePagination-displayedRows')?.textContent).toBe('1–2 от 2');
    expect(screen.getByText('Редове на страница')).toBeInTheDocument();
    expect(screen.getByLabelText('Към следващата страница')).toBeInTheDocument();
    expect(screen.getByLabelText('Към предишната страница')).toBeInTheDocument();
    expect(screen.queryByLabelText('Go to next page')).toBeNull();
    expect(document.body.textContent).not.toContain('of 2');
  });

  it('DataTable_BgLocale_TranslatesTheSortTooltip', async () => {
    await i18n.changeLanguage('bg');

    renderPopulated();

    const header = await screen.findByRole('columnheader', { name: 'Code' });
    const sortLabel = within(header).getByLabelText('Сортиране');
    expect(sortLabel).toBeInTheDocument();
    expect(within(header).queryByLabelText('Sort')).toBeNull();
  });
});
