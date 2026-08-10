import { useEffect, useMemo, useState } from 'react';
import {
  Box,
  MenuItem,
  ToggleButton,
  ToggleButtonGroup,
  Tooltip,
  Typography
} from '@mui/material';
import {
  DataGrid,
  type GridColDef,
  type GridPaginationModel
} from '@mui/x-data-grid';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { createSearchParams, useNavigate } from 'react-router-dom';
import { ListPageTemplate } from '@/components/templates';
import { DataTable, ledgerMonoColumn } from '@/components/organisms';
import { EmptyState, FormField, ForbiddenState } from '@/components/molecules';
import {
  AppTextField,
  CodeText,
  HairlineDivider,
  MoneyText,
  Panel
} from '@/components/atoms';
import { useLayoutStore } from '@/shared/stores/layout';
import { useDebouncedValue } from '@/shared/hooks/useDebouncedValue';
import { useGridLocaleText } from '@/shared/hooks/useGridLocaleText';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage, isForbiddenError } from '@/shared/utils/getApiErrorMessage';
import { MAX_PAGE_SIZE, type FilterRequest } from '@/shared/api/paging';
import { getAgingReport, searchCounterpartyBalances } from '@/features/payments/api';
import {
  parseBuckets,
  todayIso,
  validateAgingQuery,
  validateBalancesQuery
} from '@/features/payments/schema';
import {
  AGING_DIRECTIONS,
  CURRENT_BUCKET_LABEL,
  directionStringLabelKey,
  type AgingDirection,
  type AgingRowDto,
  type CounterpartyBalanceDto
} from '@/features/payments/types';

type View = 'aging' | 'balances';

/** Default page size for the counterparty-balances grid (≤ the backend cap of 200). */
const DEFAULT_PAGE_SIZE = 50;

/**
 * Quiet period before a typed bucket-boundary list is committed to the query key. `GET /api/v1/aging`
 * has NO paging and NO server-side cap (§1.6 gap 8), so an undebounced field turns every keystroke of
 * `30, 60, 90` into a full unbounded report build.
 */
const BUCKETS_DEBOUNCE_MS = 300;

/** Formats an ISO 8601 timestamp as a `yyyy-MM-dd` calendar date for display. */
function formatDate(value: string): string {
  return value.slice(0, 10);
}

/**
 * AP/AR aging (SDD-UI-FIN-002 §2.14; SDD-PAY-003 §2.4, §2.6) and counterparty balances (§2.15;
 * SDD-PAY-003 §2.7) on ONE route behind ONE shared control bar — mirroring the shipped
 * `ExchangeRatesPage`, which hosts a latest view and a range view on a single page. Both surfaces
 * require the SAME `finance.aging:read` permission and take the same as-of / direction / currency
 * inputs, so splitting them across two routes would duplicate the control bar for no gain.
 *
 * Shipped-contract details that shape this page:
 *
 * - **The aging report has NO paging and NO `FilterRequest`.** `GET /api/v1/aging` returns one
 *   `AgingReportDto` carrying ALL rows, so the table renders the full set and no `page`/`pageSize` is
 *   invented for it (§1.4 trap 15). It is, however, unbounded — narrowing by counterparty/currency
 *   keeps it tractable (§1.6 gap 8).
 * - **`buckets` binds ONLY as REPEATED query values** (`?buckets=30&buckets=60&buckets=90`), which the
 *   api layer guarantees, and is OMITTED entirely when the operator has not customized it so the server
 *   applies its documented default of `30, 60, 90` (§1.4 trap 7, §2.14).
 * - **Bucket columns are built from the response's `bucketLabels`, never hard-coded.** The boundaries
 *   are configurable, so four boundaries yield SIX columns. Labels are server-generated DATA, not i18n
 *   keys: only `"Current"` is translated; the numeric range labels render verbatim (§1.4 trap 16).
 * - **Only BASE-currency figures may be summed across rows.** The report-level `totals` are
 *   base-currency only by design, so no cross-currency transactional total is computed or displayed,
 *   and the totals row names `baseCurrencyCode` (§2.14).
 * - **The report is period-status-agnostic and invoice-only** — a closed period's invoices are still
 *   aged, and unallocated payment cash is NOT netted in, so no balance is ever negative. Both are
 *   stated in visible translated help text so the numbers are not misread.
 * - **The balances grid exposes NO user sorting.** Its rows are GROUPED, so no `[Sortable]` entity
 *   surface applies; the server orders by `BaseOutstanding` desc then the composite grouping key
 *   (§1.6 gap 10). It also has NO counterparty narrowing (§1.6 gap 9) — one counterparty's detail is
 *   reached through the `/open-items` drill-down.
 * - **Empty is a `200`, never a `404`.** A counterparty with zero in-scope outstanding is omitted by the
 *   server; the UI never synthesizes zero rows and renders the editorial empty state with zero totals.
 *
 * Every counterparty renders as a raw GUID in the mono face: there is no name-enrichment endpoint
 * (`SDD-INT-WH-002` deferred), which is the largest usability gap in this feature and is NOT worked
 * around with a fake lookup (§1.6 gap 1). A `403` on either surface renders the editorial forbidden
 * state — and each surface reaches its own conclusion, since `finance.aging:read` is separate from
 * `finance.payment:read` (§2.17).
 */
export function AgingReportPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const isCompact = useLayoutStore((s) => s.isCompact);
  const density = useLayoutStore((s) => s.density);

  const [view, setView] = useState<View>('aging');
  const [asOfDate, setAsOfDate] = useState(todayIso());
  const [direction, setDirection] = useState<AgingDirection>('AR');
  const [counterpartyId, setCounterpartyId] = useState('');
  const [currencyCode, setCurrencyCode] = useState('');
  const [bucketsText, setBucketsText] = useState('');

  const [paginationModel, setPaginationModel] = useState<GridPaginationModel>({
    page: 0,
    pageSize: DEFAULT_PAGE_SIZE
  });

  /**
   * The boundaries are parsed from the DEBOUNCED text, so both the request and the inline validation
   * settle once per entry rather than once per keystroke (§1.6 gap 8). The field itself stays
   * immediately responsive — only what feeds the query key waits.
   */
  const committedBucketsText: string = useDebouncedValue(bucketsText, BUCKETS_DEBOUNCE_MS);
  const buckets: number[] = useMemo(() => parseBuckets(committedBucketsText), [committedBucketsText]);

  const agingErrors = validateAgingQuery({
    asOfDate,
    direction,
    counterpartyId,
    currencyCode,
    buckets
  });
  const balanceErrors = validateBalancesQuery({ asOfDate, direction, currencyCode });

  const errors = view === 'aging' ? agingErrors : balanceErrors;
  const agingValid: boolean = Object.keys(agingErrors).length === 0;
  const balancesValid: boolean = Object.keys(balanceErrors).length === 0;

  const agingQuery = useQuery({
    queryKey: ['aging', asOfDate, direction, counterpartyId, currencyCode, buckets],
    queryFn: () =>
      getAgingReport({
        asOfDate,
        direction,
        counterpartyId: counterpartyId || undefined,
        currencyCode: currencyCode || undefined,
        // An EMPTY list means "not customized" — the param is omitted so the server default applies.
        buckets: buckets.length > 0 ? buckets : undefined
      }),
    enabled: view === 'aging' && agingValid,
    staleTime: 0
  });

  const balancesRequest = useMemo<FilterRequest>(
    () => ({
      page: paginationModel.page + 1,
      pageSize: Math.min(paginationModel.pageSize, MAX_PAGE_SIZE)
    }),
    [paginationModel]
  );

  const balancesQuery = useQuery({
    queryKey: ['counterparty-balances', asOfDate, direction, currencyCode, balancesRequest],
    queryFn: () =>
      searchCounterpartyBalances(
        { asOfDate, direction, currencyCode: currencyCode || undefined },
        balancesRequest
      ),
    enabled: view === 'balances' && balancesValid,
    placeholderData: (prev) => prev,
    staleTime: 0
  });

  const activeError: unknown = view === 'aging' ? agingQuery.error : balancesQuery.error;
  const forbidden: boolean = isForbiddenError(activeError);

  useEffect(() => {
    if (activeError && !isForbiddenError(activeError)) {
      notification.error(getApiErrorMessage(activeError, t));
    }
  }, [activeError, t]);

  function handleViewChange(_event: React.MouseEvent<HTMLElement>, next: View | null) {
    if (next) {
      setView(next);
    }
  }

  /** Drills a report row into `/open-items`, carrying exactly the narrowings that endpoint supports. */
  function drillDown(row: AgingRowDto) {
    const search: string = createSearchParams({
      counterpartyId: row.counterpartyId,
      currencyCode: row.currencyCode,
      direction,
      asOfDate
    }).toString();
    navigate({ pathname: '/open-items', search: `?${search}` });
  }

  /** Translates only the `Current` bucket; every other label is server data and renders verbatim. */
  function bucketHeader(label: string): string {
    return label === CURRENT_BUCKET_LABEL ? t('aging.bucket_Current') : label;
  }

  const report = agingQuery.data;
  const bucketLabels: string[] = report?.bucketLabels ?? [];

  const agingColumns = useMemo<GridColDef<AgingRowDto>[]>(() => {
    const bucketColumns: GridColDef<AgingRowDto>[] = bucketLabels.map((label, index) => ({
      field: `bucket_${index}`,
      headerName: bucketHeader(label),
      width: 140,
      ...ledgerMonoColumn,
      sortable: false,
      renderCell: (params) => {
        const bucket =
          params.row.buckets.find((candidate) => candidate.label === label) ??
          params.row.buckets[index];
        if (!bucket) {
          return <span>—</span>;
        }
        return (
          <Tooltip
            title={`${bucketHeader(label)} · ${bucket.itemCount}`}
            key={`${params.row.counterpartyId}-${label}`}
          >
            <span>
              <MoneyText amount={bucket.outstanding} />
            </span>
          </Tooltip>
        );
      }
    }));

    return [
      {
        field: 'counterpartyId',
        headerName: t('aging.counterparty'),
        flex: 1,
        minWidth: 260,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        sortable: false,
        renderCell: (params) => (
          <Tooltip title={params.row.counterpartyId}>
            <span>
              <CodeText>{params.row.counterpartyId}</CodeText>
            </span>
          </Tooltip>
        )
      },
      {
        field: 'currencyCode',
        headerName: t('aging.currency'),
        width: 90,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) => <CodeText>{params.row.currencyCode}</CodeText>
      },
      {
        field: 'openItemCount',
        headerName: t('aging.openItemCount'),
        width: 110,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) => <CodeText>{params.row.openItemCount}</CodeText>
      },
      ...bucketColumns,
      {
        field: 'totalOutstanding',
        headerName: t('aging.totalOutstanding'),
        width: 150,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) => (
          <MoneyText amount={params.row.totalOutstanding} currency={params.row.currencyCode} />
        )
      },
      {
        field: 'totalBaseOutstanding',
        headerName: t('aging.totalBaseOutstanding'),
        width: 160,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) => (
          <MoneyText
            amount={params.row.totalBaseOutstanding}
            currency={params.row.baseCurrencyCode}
          />
        )
      }
    ];
    // Keyed on the JOINED labels rather than the array identity: the response is refetched on every
    // control-bar change, and rebuilding the column set only matters when the LABELS actually differ.
  }, [t, bucketLabels.join('|')]);

  const balanceColumns = useMemo<GridColDef<CounterpartyBalanceDto>[]>(
    () => [
      {
        field: 'counterpartyId',
        headerName: t('balances.counterparty'),
        flex: 1,
        minWidth: 260,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        sortable: false,
        renderCell: (params) => (
          <Tooltip title={params.row.counterpartyId}>
            <span>
              <CodeText>{params.row.counterpartyId}</CodeText>
            </span>
          </Tooltip>
        )
      },
      {
        field: 'currencyCode',
        headerName: t('balances.currency'),
        width: 90,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) => <CodeText>{params.row.currencyCode}</CodeText>
      },
      {
        field: 'openItemCount',
        headerName: t('balances.openItemCount'),
        width: 110,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) => <CodeText>{params.row.openItemCount}</CodeText>
      },
      {
        field: 'outstanding',
        headerName: t('balances.outstanding'),
        width: 150,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) => (
          <MoneyText amount={params.row.outstanding} currency={params.row.currencyCode} />
        )
      },
      {
        field: 'baseOutstanding',
        headerName: t('balances.baseOutstanding'),
        width: 160,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) => (
          <MoneyText amount={params.row.baseOutstanding} currency={params.row.baseCurrencyCode} />
        )
      },
      {
        field: 'overdueOutstanding',
        headerName: t('balances.overdueOutstanding'),
        width: 160,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) => (
          <MoneyText amount={params.row.overdueOutstanding} currency={params.row.currencyCode} />
        )
      },
      {
        field: 'baseOverdueOutstanding',
        headerName: t('balances.baseOverdueOutstanding'),
        width: 170,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) => (
          <MoneyText
            amount={params.row.baseOverdueOutstanding}
            currency={params.row.baseCurrencyCode}
          />
        )
      },
      {
        field: 'oldestDueDate',
        headerName: t('balances.oldestDueDate'),
        width: 150,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        sortable: false,
        renderCell: (params) =>
          params.row.oldestDueDate ? (
            <CodeText>{formatDate(params.row.oldestDueDate)}</CodeText>
          ) : (
            <Box component="span" sx={{ color: 'text.secondary' }}>
              {t('balances.noOpenItems')}
            </Box>
          )
      }
    ],
    [t]
  );

  const agingRows: AgingRowDto[] = report?.rows ?? [];
  const showAgingEmpty: boolean = !agingQuery.isFetching && agingRows.length === 0;
  const agingLocaleText = useGridLocaleText(t('aging.empty'));

  return (
    <ListPageTemplate
      overline={t('nav.section')}
      title={view === 'aging' ? t('aging.title') : t('balances.title')}
    >
      <Panel sx={{ mb: isCompact ? 2 : 3 }}>
        <ToggleButtonGroup
          value={view}
          exclusive
          onChange={handleViewChange}
          size={isCompact ? 'small' : 'medium'}
          sx={{ mb: isCompact ? 2 : 2.5 }}
        >
          <ToggleButton value="aging">{t('aging.title')}</ToggleButton>
          <ToggleButton value="balances">{t('balances.title')}</ToggleButton>
        </ToggleButtonGroup>

        <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap', alignItems: 'flex-end' }}>
          <Box sx={{ width: 190 }}>
            <FormField
              label={t('aging.asOfDate')}
              required
              error={errors.asOfDate ? t(errors.asOfDate) : undefined}
            >
              <AppTextField
                type="date"
                value={asOfDate}
                error={Boolean(errors.asOfDate)}
                onChange={(e) => setAsOfDate(e.target.value)}
                InputLabelProps={{ shrink: true }}
              />
            </FormField>
          </Box>

          <Box sx={{ width: 150 }}>
            <FormField
              label={t('aging.direction')}
              required
              error={errors.direction ? t(errors.direction) : undefined}
            >
              <AppTextField
                select
                value={direction}
                error={Boolean(errors.direction)}
                onChange={(e) => setDirection(e.target.value as AgingDirection)}
              >
                {AGING_DIRECTIONS.map((d) => (
                  <MenuItem key={d} value={d}>
                    {t(directionStringLabelKey(d))}
                  </MenuItem>
                ))}
              </AppTextField>
            </FormField>
          </Box>

          {view === 'aging' ? (
            <Box sx={{ flex: '1 1 300px', minWidth: 260 }}>
              <FormField
                label={t('aging.counterparty')}
                error={agingErrors.counterpartyId ? t(agingErrors.counterpartyId) : undefined}
              >
                <AppTextField
                  value={counterpartyId}
                  error={Boolean(agingErrors.counterpartyId)}
                  placeholder={t('payments.counterpartyPlaceholder')}
                  onChange={(e) => setCounterpartyId(e.target.value)}
                />
              </FormField>
            </Box>
          ) : null}

          <Box sx={{ width: 130 }}>
            <FormField
              label={t('aging.currency')}
              error={errors.currencyCode ? t(errors.currencyCode) : undefined}
            >
              <AppTextField
                value={currencyCode}
                error={Boolean(errors.currencyCode)}
                onChange={(e) => setCurrencyCode(e.target.value.toUpperCase())}
              />
            </FormField>
          </Box>

          {view === 'aging' ? (
            <Box sx={{ width: 220 }}>
              <FormField
                label={t('aging.buckets')}
                error={agingErrors.buckets ? t(agingErrors.buckets) : undefined}
              >
                <AppTextField
                  value={bucketsText}
                  error={Boolean(agingErrors.buckets)}
                  placeholder="30, 60, 90"
                  onChange={(e) => setBucketsText(e.target.value)}
                />
              </FormField>
            </Box>
          ) : null}
        </Box>

        <Box sx={{ mt: isCompact ? 1.5 : 2, display: 'flex', flexDirection: 'column', gap: 0.5 }}>
          {view === 'aging' ? (
            <>
              <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                {t('aging.bucketsHint')}
              </Typography>
              <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                {t('aging.bucketsDefaultHint')}
              </Typography>
              <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                {t('aging.baseCurrencyOnlyHint')}
              </Typography>
              <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                {t('aging.periodAgnosticHint')}
              </Typography>
              <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                {t('aging.invoiceOnlyHint')}
              </Typography>
              <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                {t('aging.drillDown')}
              </Typography>
            </>
          ) : (
            <>
              <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                {t('balances.overdueDefinitionHint')}
              </Typography>
              <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                {t('balances.matchesAgingHint')}
              </Typography>
              <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                {t('balances.noSortingHint')}
              </Typography>
            </>
          )}
        </Box>
      </Panel>

      {forbidden ? (
        <ForbiddenState
          title={view === 'aging' ? t('aging.forbidden') : t('balances.forbidden')}
          description={t('aging.forbiddenHint')}
        />
      ) : view === 'aging' ? (
        <Panel flush>
          {/*
            The empty state replaces the grid instead of riding in its `noRowsOverlay`: that overlay
            lives inside the `overflow: hidden` virtual scroller and is only two row-heights tall, which
            clipped this description (the longest of the three payments routes) by 22px. Rendering it
            outside the grid removes the clipping container rather than guessing a taller height that
            has to hold for both densities and the longer Bulgarian copy.
          */}
          {showAgingEmpty ? (
            <EmptyState
              framed={false}
              title={t('aging.empty')}
              description={t('aging.emptyHint')}
            />
          ) : (
            <DataGrid<AgingRowDto>
              autoHeight
              density={density}
              rows={agingRows}
              columns={agingColumns}
              getRowId={(row) => `${row.counterpartyId}|${row.currencyCode}`}
              loading={agingQuery.isFetching}
              onRowClick={(params) => drillDown(params.row)}
              hideFooter
              disableColumnMenu
              disableRowSelectionOnClick
              localeText={agingLocaleText}
              sx={{ border: 'none', '& .MuiDataGrid-row': { cursor: 'pointer' } }}
              slotProps={{
                loadingOverlay: { variant: 'linear-progress', noRowsVariant: 'skeleton' }
              }}
            />
          )}

          <HairlineDivider />
          <Box
            sx={{
              px: isCompact ? 2 : 3,
              py: isCompact ? 1.5 : 2,
              display: 'flex',
              flexDirection: 'column',
              gap: 1
            }}
          >
            <Typography
              component="span"
              sx={{
                fontWeight: 600,
                textTransform: 'uppercase',
                letterSpacing: '0.06em',
                fontSize: '0.8125rem'
              }}
            >
              {t('aging.reportTotals')}
              {report ? ` · ${report.baseCurrencyCode}` : ''}
            </Typography>
            <Box sx={{ display: 'flex', gap: 3, flexWrap: 'wrap' }}>
              {(report?.totals ?? []).map((total) => (
                <Box key={total.label}>
                  <Typography variant="overline" component="div">
                    {bucketHeader(total.label)}
                  </Typography>
                  <MoneyText amount={total.baseOutstanding} />
                </Box>
              ))}
              <Box sx={{ ml: 'auto', textAlign: 'right' }}>
                <Typography variant="overline" component="div">
                  {t('aging.grandTotalBaseOutstanding')}
                </Typography>
                <MoneyText
                  amount={report?.grandTotalBaseOutstanding ?? 0}
                  currency={report?.baseCurrencyCode}
                  sx={{ fontWeight: 600 }}
                />
              </Box>
            </Box>
          </Box>
        </Panel>
      ) : (
        <DataTable<CounterpartyBalanceDto>
          rows={balancesQuery.data?.items ?? []}
          columns={balanceColumns}
          getRowId={(row) => `${row.counterpartyId}|${row.currencyCode}`}
          loading={balancesQuery.isFetching}
          rowCount={balancesQuery.data?.totalCount ?? 0}
          paginationModel={paginationModel}
          onPaginationModelChange={setPaginationModel}
          sortModel={[]}
          onSortModelChange={() => undefined}
          emptyTitle={t('balances.empty')}
          emptyDescription={t('balances.emptyHint')}
        />
      )}
    </ListPageTemplate>
  );
}
