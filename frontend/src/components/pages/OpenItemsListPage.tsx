import { useEffect, useMemo, useState } from 'react';
import { Box, FormControlLabel, MenuItem, Switch, Typography } from '@mui/material';
import { type GridColDef, type GridPaginationModel, type GridSortModel } from '@mui/x-data-grid';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { ListPageTemplate } from '@/components/templates';
import { DataTable, ledgerMonoColumn } from '@/components/organisms';
import { FormField, ForbiddenState } from '@/components/molecules';
import { AppTextField, CodeText, MoneyText, Panel } from '@/components/atoms';
import { useLayoutStore } from '@/shared/stores/layout';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage, isForbiddenError } from '@/shared/utils/getApiErrorMessage';
import { MAX_PAGE_SIZE, type FilterRequest } from '@/shared/api/paging';
import { searchOpenItems } from '@/features/payments/api';
import { validateOpenItemQuery } from '@/features/payments/schema';
import {
  AGING_DIRECTIONS,
  directionStringLabelKey,
  settlementStatusLabelKey,
  type AgingDirection,
  type OpenItemDto,
  type OpenItemQuery
} from '@/features/payments/types';

/** Default page size for the open-items grid (≤ the backend cap of 200, SDD-INFRA-005). */
const DEFAULT_PAGE_SIZE = 50;

/** Formats an ISO 8601 timestamp as a `yyyy-MM-dd` calendar date for display. */
function formatDate(value: string): string {
  return value.slice(0, 10);
}

/**
 * The AP/AR open-items worklist (SDD-UI-FIN-002 §2.13; SDD-PAY-003 §2.5). Oldest-due-first so the page
 * reads as a collection worklist, with the `asOfDate` / `direction` / `counterpartyId` /
 * `currencyCode` / `overdueOnly` narrowings — every one of them OPTIONAL, because `asOfDate` defaults
 * to today server-side and an omitted narrowing simply widens the list.
 *
 * Shipped-contract details that shape this page:
 *
 * - **`toFilterParams` does NOT carry the narrowings.** This endpoint binds BOTH a `FilterRequest` AND
 *   an `OpenItemQueryRequest` from the SAME query string, so the api layer merges them (§1.4 trap 8).
 * - **`direction` is sent as the STRING `"AR"`/`"AP"`**, never the numeric `PaymentDirection`.
 * - **There is NO search box.** `InvoiceOpenItem` declares no `[Searchable]` property, so `search`
 *   would have nothing to match (§1.6 gap 11). Filter/sort target only the opt-in surface, and
 *   `CounterpartyId` is `[Filterable]`-only, so its column exposes no sort.
 * - **`daysPastDue ≤ 0` reads "not yet due"**, never a negative number, and corresponds to the
 *   `Current` bucket — due exactly ON `asOfDate` is `Current`, never `1-30` (§2.18).
 * - **`baseOutstanding` is a BOOKING-RATE figure**, not a live revaluation, which the help text states
 *   (§1.6 gap 19).
 * - **The projection is EVENTUALLY CONSISTENT and a confirmed credit note is absent PERMANENTLY by
 *   design.** Both are stated in visible translated help text so neither is misread as an error or as
 *   projection lag (§2.13).
 * - **A future `asOfDate` is blocked client-side**; `INVALID_AGING_AS_OF_DATE` is mapped if one ever
 *   reaches the server.
 *
 * The page also accepts `counterpartyId` / `currencyCode` / `direction` / `asOfDate` search params so
 * an aging-report row can drill straight into its own open items (§2.14).
 */
export function OpenItemsListPage() {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();
  const isCompact = useLayoutStore((s) => s.isCompact);

  const [asOfDate, setAsOfDate] = useState(searchParams.get('asOfDate') ?? '');
  const [direction, setDirection] = useState(searchParams.get('direction') ?? '');
  const [counterpartyId, setCounterpartyId] = useState(searchParams.get('counterpartyId') ?? '');
  const [currencyCode, setCurrencyCode] = useState(searchParams.get('currencyCode') ?? '');
  const [overdueOnly, setOverdueOnly] = useState(false);

  const [paginationModel, setPaginationModel] = useState<GridPaginationModel>({
    page: 0,
    pageSize: DEFAULT_PAGE_SIZE
  });
  const [sortModel, setSortModel] = useState<GridSortModel>([{ field: 'dueDate', sort: 'asc' }]);

  const errors = validateOpenItemQuery({ asOfDate, counterpartyId, currencyCode });
  const queryValid: boolean = Object.keys(errors).length === 0;

  const narrowing = useMemo<OpenItemQuery>(
    () => ({
      asOfDate: asOfDate || undefined,
      direction: direction === '' ? undefined : (direction as AgingDirection),
      counterpartyId: counterpartyId || undefined,
      currencyCode: currencyCode || undefined,
      overdueOnly
    }),
    [asOfDate, direction, counterpartyId, currencyCode, overdueOnly]
  );

  const filterRequest = useMemo<FilterRequest>(
    () => ({
      page: paginationModel.page + 1,
      pageSize: Math.min(paginationModel.pageSize, MAX_PAGE_SIZE),
      sort: sortModel
        .filter((s) => s.sort)
        .map((s) => ({ field: s.field, direction: s.sort === 'desc' ? 'desc' : 'asc' }))
    }),
    [paginationModel, sortModel]
  );

  const { data, isFetching, error } = useQuery({
    queryKey: ['open-items', narrowing, filterRequest],
    queryFn: () => searchOpenItems(narrowing, filterRequest),
    enabled: queryValid,
    placeholderData: (prev) => prev
  });

  const forbidden: boolean = isForbiddenError(error);

  useEffect(() => {
    if (error && !isForbiddenError(error)) {
      notification.error(getApiErrorMessage(error, t));
    }
  }, [error, t]);

  const columns = useMemo<GridColDef<OpenItemDto>[]>(
    () => [
      {
        field: 'documentNumber',
        headerName: t('openItems.documentNumber'),
        width: 150,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        renderCell: (params) => <CodeText>{params.row.documentNumber}</CodeText>
      },
      {
        field: 'documentType',
        headerName: t('openItems.documentType'),
        width: 140,
        renderCell: (params) => params.row.documentType
      },
      {
        field: 'direction',
        headerName: t('openItems.direction'),
        width: 80,
        renderCell: (params) => t(directionStringLabelKey(params.row.direction))
      },
      {
        field: 'counterpartyId',
        headerName: t('openItems.counterparty'),
        flex: 1,
        minWidth: 200,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        sortable: false,
        renderCell: (params) => <CodeText>{params.row.counterpartyId}</CodeText>
      },
      {
        field: 'currencyCode',
        headerName: t('openItems.currency'),
        width: 90,
        ...ledgerMonoColumn,
        renderCell: (params) => <CodeText>{params.row.currencyCode}</CodeText>
      },
      {
        field: 'grossTotal',
        headerName: t('openItems.grossTotal'),
        width: 140,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) => <MoneyText amount={params.row.grossTotal} />
      },
      {
        field: 'settledAmount',
        headerName: t('openItems.settledAmount'),
        width: 140,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) => <MoneyText amount={params.row.settledAmount} />
      },
      {
        field: 'outstanding',
        headerName: t('openItems.outstanding'),
        width: 150,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) => (
          <MoneyText amount={params.row.outstanding} currency={params.row.currencyCode} />
        )
      },
      {
        field: 'baseOutstanding',
        headerName: t('openItems.baseOutstanding'),
        width: 150,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) => (
          <MoneyText amount={params.row.baseOutstanding} currency={params.row.baseCurrencyCode} />
        )
      },
      {
        field: 'issueDate',
        headerName: t('openItems.issueDate'),
        width: 130,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        renderCell: (params) => <CodeText>{formatDate(params.row.issueDate)}</CodeText>
      },
      {
        field: 'dueDate',
        headerName: t('openItems.dueDate'),
        width: 130,
        ...ledgerMonoColumn,
        headerAlign: 'left',
        align: 'left',
        renderCell: (params) => <CodeText>{formatDate(params.row.dueDate)}</CodeText>
      },
      {
        field: 'daysPastDue',
        headerName: t('openItems.daysPastDue'),
        width: 130,
        ...ledgerMonoColumn,
        sortable: false,
        renderCell: (params) =>
          params.row.daysPastDue > 0 ? (
            <CodeText>{params.row.daysPastDue}</CodeText>
          ) : (
            <Box component="span" sx={{ color: 'text.secondary' }}>
              {t('openItems.notYetDue')}
            </Box>
          )
      },
      {
        field: 'agingBucket',
        headerName: t('openItems.agingBucket'),
        width: 110,
        sortable: false,
        renderCell: (params) => <CodeText>{params.row.agingBucket}</CodeText>
      },
      {
        field: 'settlementStatus',
        headerName: t('openItems.settlementStatus'),
        width: 150,
        sortable: false,
        renderCell: (params) => t(settlementStatusLabelKey(params.row.settlementStatus))
      },
      {
        field: 'invoiceStatus',
        headerName: t('openItems.invoiceStatus'),
        width: 120,
        renderCell: (params) => params.row.invoiceStatus
      }
    ],
    [t]
  );

  return (
    <ListPageTemplate overline={t('nav.section')} title={t('openItems.title')}>
      <Panel sx={{ mb: isCompact ? 2 : 3 }}>
        <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap', alignItems: 'flex-end' }}>
          <Box sx={{ width: 190 }}>
            <FormField
              label={t('openItems.asOfDate')}
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
            <FormField label={t('openItems.direction')}>
              <AppTextField
                select
                value={direction}
                onChange={(e) => setDirection(e.target.value)}
              >
                <MenuItem value="">{t('openItems.allDirections')}</MenuItem>
                {AGING_DIRECTIONS.map((d) => (
                  <MenuItem key={d} value={d}>
                    {t(directionStringLabelKey(d))}
                  </MenuItem>
                ))}
              </AppTextField>
            </FormField>
          </Box>

          <Box sx={{ flex: '1 1 300px', minWidth: 260 }}>
            <FormField
              label={t('openItems.counterparty')}
              error={errors.counterpartyId ? t(errors.counterpartyId) : undefined}
            >
              <AppTextField
                value={counterpartyId}
                error={Boolean(errors.counterpartyId)}
                placeholder={t('payments.counterpartyPlaceholder')}
                onChange={(e) => setCounterpartyId(e.target.value)}
              />
            </FormField>
          </Box>

          <Box sx={{ width: 130 }}>
            <FormField
              label={t('openItems.currency')}
              error={errors.currencyCode ? t(errors.currencyCode) : undefined}
            >
              <AppTextField
                value={currencyCode}
                error={Boolean(errors.currencyCode)}
                onChange={(e) => setCurrencyCode(e.target.value.toUpperCase())}
              />
            </FormField>
          </Box>

          <FormControlLabel
            control={
              <Switch
                checked={overdueOnly}
                size={isCompact ? 'small' : 'medium'}
                onChange={(e) => setOverdueOnly(e.target.checked)}
              />
            }
            label={t('openItems.overdueOnly')}
          />
        </Box>

        <Box sx={{ mt: isCompact ? 1.5 : 2, display: 'flex', flexDirection: 'column', gap: 0.5 }}>
          <Typography variant="caption" sx={{ color: 'text.secondary' }}>
            {t('openItems.eventualConsistencyHint')}
          </Typography>
          <Typography variant="caption" sx={{ color: 'text.secondary' }}>
            {t('openItems.creditNoteExcludedHint')}
          </Typography>
          <Typography variant="caption" sx={{ color: 'text.secondary' }}>
            {t('openItems.bookingRateHint')}
          </Typography>
        </Box>
      </Panel>

      {forbidden ? (
        <ForbiddenState title={t('openItems.forbidden')} description={t('payments.forbiddenHint')} />
      ) : (
        <DataTable<OpenItemDto>
          rows={data?.items ?? []}
          columns={columns}
          getRowId={(row) => row.invoiceId}
          loading={isFetching}
          rowCount={data?.totalCount ?? 0}
          paginationModel={paginationModel}
          onPaginationModelChange={setPaginationModel}
          sortModel={sortModel}
          onSortModelChange={setSortModel}
          emptyTitle={t('openItems.empty')}
          emptyDescription={t('openItems.emptyHint')}
        />
      )}
    </ListPageTemplate>
  );
}
