using AutoMapper;
using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Common.Settlement;
using Finance.Country.Abstractions;
using Finance.GenericFiltering;
using Finance.GenericFiltering.Exceptions;
using Finance.GenericFiltering.Models;
using Finance.Payments.API.Interfaces;
using Finance.Payments.API.Validators;
using Finance.Payments.DBModel;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Finance.Payments.API.Services;

/// <summary>
/// Default <see cref="IAgingService"/> (SDD-PAY-003): a read-only aggregation over the SDD-PAY-002
/// <c>InvoiceOpenItem</c> projection, joined to the allocation and payment rows only when a HISTORICAL as-of date
/// requires replaying settlement. It owns no table, publishes no event, writes no audit row, runs no workflow, and
/// allocates no document number — the same shape SDD-FIN-003 used to add the ledger reads inside the Journal
/// service.
/// <para><b>Two inclusion predicates, stated once and shared by all three endpoints.</b> An item counts only when
/// its mirrored status is in the explicit POSITIVE set <c>{ Confirmed, Posted }</c> (never as a negation of the
/// terminal statuses) and only when its document type is settleable by some payment document type. The settleable
/// set is DERIVED from the shared <see cref="SettlementPairing"/> table rather than re-listed here, so widening or
/// narrowing the pairing can never leave the aged population and the allocatable population disagreeing. It is
/// defense-in-depth: SDD-PAY-002's confirmation consumer already refuses to create the row.</para>
/// <para><b>The aggregation is one round trip.</b> Every predicate — status, document type, issue date, direction,
/// counterparty, currency, overdue-only, and the strictly-positive outstanding amount — is evaluated in SQL, and
/// the as-of settled amount is produced by the SAME server-side projection. Only the in-scope rows cross the wire;
/// there is no per-counterparty and no per-bucket query. Bucketing and grouping then run in memory over those rows
/// through the pure <see cref="AgingBucketCalculator"/>, which is why bucket assignment is testable without a
/// database.</para>
/// <para><b>One shared grouping path.</b> <c>/aging</c> and <c>/counterparty-balances</c> both fold the same
/// valuations into the same <see cref="CounterpartyAggregate"/> set, so they are structurally incapable of
/// reporting different totals for the same (counterparty, currency) pair.</para>
/// <para><b>Nothing is cached and nothing is state-changing</b>, which is why no cache service, workflow engine,
/// audit service, publish endpoint, or sequence generator is a dependency here.</para>
/// </summary>
public sealed class AgingService : IAgingService
{
    private const string DueDateSortField = nameof(InvoiceOpenItem.DueDate);
    private const string AscendingSortDirection = "asc";
    private const string ConfirmedInvoiceStatus = nameof(InvoiceStatus.Confirmed);
    private const string PostedInvoiceStatus = nameof(InvoiceStatus.Posted);

    private static readonly IReadOnlyList<string> SettleableDocumentTypes = BuildSettleableDocumentTypes();

    private readonly PaymentsDbContext _db;
    private readonly IMapper _mapper;
    private readonly AgingBucketCalculator _bucketCalculator;
    private readonly SettlementStatusCalculator _settlement;
    private readonly ICountryStrategy _countryStrategy;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgingService> _logger;

    /// <summary>Creates a new <see cref="AgingService"/>.</summary>
    /// <param name="db">The payments database context (read-only use).</param>
    /// <param name="mapper">The AutoMapper instance mapping the projection row onto its report DTO.</param>
    /// <param name="bucketCalculator">The pure aging bucket calculator.</param>
    /// <param name="settlement">The single derived-settlement-status calculator (SDD-PAY-002 §2.8).</param>
    /// <param name="countryStrategy">The country strategy owning the base currency and monetary rounding.</param>
    /// <param name="timeProvider">The clock supplying "today" for the as-of path choice and the future-date guard.</param>
    /// <param name="logger">Structured logger for read-path diagnostics; counterparty identifiers are never logged.</param>
    public AgingService(
        PaymentsDbContext db,
        IMapper mapper,
        AgingBucketCalculator bucketCalculator,
        SettlementStatusCalculator settlement,
        ICountryStrategy countryStrategy,
        TimeProvider timeProvider,
        ILogger<AgingService> logger)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(bucketCalculator);
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(countryStrategy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _db = db;
        _mapper = mapper;
        _bucketCalculator = bucketCalculator;
        _settlement = settlement;
        _countryStrategy = countryStrategy;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<OpenItemDto>>> GetOpenItemsAsync(
        OpenItemQueryRequest query,
        FilterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);

        Result validated = ValidateOpenItemQuery(query, request);
        if (!validated.IsSuccess)
        {
            return Result<PagedResult<OpenItemDto>>.Failure(validated.ErrorCode!, validated.Detail);
        }

        Result<IReadOnlyList<AgingBucketDefinition>> buckets = _bucketCalculator.Build(dayBoundaries: null);
        if (!buckets.IsSuccess)
        {
            return Result<PagedResult<OpenItemDto>>.Failure(buckets.ErrorCode!, buckets.Detail);
        }

        AgingScope scope = BuildScope(query);

        try
        {
            PagedResult<OpenItemDto> page =
                await BuildOpenItemPageAsync(scope, buckets.Value!, ApplyDefaultSort(request), cancellationToken)
                    .ConfigureAwait(false);
            return Result<PagedResult<OpenItemDto>>.Success(page);
        }
        catch (FilterValidationException ex)
        {
            return Result<PagedResult<OpenItemDto>>.Failure(ex.ErrorCode, ex.Detail);
        }
    }

    /// <inheritdoc />
    public async Task<Result<AgingReportDto>> GetAgingAsync(
        AgingReportQueryRequest query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result validated = ValidateReportQuery(query);
        if (!validated.IsSuccess)
        {
            return Result<AgingReportDto>.Failure(validated.ErrorCode!, validated.Detail);
        }

        Result<IReadOnlyList<AgingBucketDefinition>> buckets = _bucketCalculator.Build(query.Buckets);
        if (!buckets.IsSuccess)
        {
            return Result<AgingReportDto>.Failure(buckets.ErrorCode!, buckets.Detail);
        }

        AgingScope scope = BuildScope(query);

        IReadOnlyList<CounterpartyAggregate> aggregates =
            await AggregateAsync(scope, buckets.Value!, cancellationToken).ConfigureAwait(false);

        return Result<AgingReportDto>.Success(ComposeReport(
            scope,
            buckets.Value!,
            _bucketCalculator.ResolveBoundaries(query.Buckets),
            aggregates));
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<CounterpartyBalanceDto>>> GetCounterpartyBalancesAsync(
        CounterpartyBalanceQueryRequest query,
        FilterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);

        Result validated = ValidateBalanceQuery(query, request);
        if (!validated.IsSuccess)
        {
            return Result<PagedResult<CounterpartyBalanceDto>>.Failure(validated.ErrorCode!, validated.Detail);
        }

        Result<IReadOnlyList<AgingBucketDefinition>> buckets = _bucketCalculator.Build(dayBoundaries: null);
        if (!buckets.IsSuccess)
        {
            return Result<PagedResult<CounterpartyBalanceDto>>.Failure(buckets.ErrorCode!, buckets.Detail);
        }

        AgingScope scope = BuildScope(query);

        IReadOnlyList<CounterpartyAggregate> aggregates =
            await AggregateAsync(scope, buckets.Value!, cancellationToken).ConfigureAwait(false);

        return Result<PagedResult<CounterpartyBalanceDto>>.Success(ComposeBalances(scope, aggregates, request));
    }

    /// <summary>
    /// Materializes the in-scope open items once and folds them into the SHARED (counterparty, currency)
    /// aggregates that both report endpoints render. This is the single aggregation path SDD-PAY-003 §2.7
    /// requires, and the single round trip §2.6 requires.
    /// </summary>
    /// <param name="scope">The validated narrowing of the read.</param>
    /// <param name="buckets">The effective buckets in bucket order.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The aggregates ordered by base outstanding descending, then counterparty, then currency.</returns>
    private async Task<IReadOnlyList<CounterpartyAggregate>> AggregateAsync(
        AgingScope scope,
        IReadOnlyList<AgingBucketDefinition> buckets,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OpenItemValuation> valuations =
            await LoadValuationsAsync(scope, buckets, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Aggregated {OpenItemCount} open items as of {AsOfDate} for direction {Direction} using {BucketCount} buckets.",
            valuations.Count,
            scope.AsOfDate,
            scope.Direction,
            buckets.Count);

        return GroupByCounterparty(valuations, buckets.Count);
    }

    private async Task<IReadOnlyList<OpenItemValuation>> LoadValuationsAsync(
        AgingScope scope,
        IReadOnlyList<AgingBucketDefinition> buckets,
        CancellationToken cancellationToken)
    {
        List<OpenItemAggregateRow> rows = await BuildOutstandingQuery(BuildScopedQuery(scope), scope)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row => Valuate(row, scope, buckets))];
    }

    private async Task<PagedResult<OpenItemDto>> BuildOpenItemPageAsync(
        AgingScope scope,
        IReadOnlyList<AgingBucketDefinition> buckets,
        FilterRequest request,
        CancellationToken cancellationToken)
    {
        IQueryable<InvoiceOpenItem> filtered = BuildScopedQuery(scope).ApplyFilterWithoutPaging(request);
        IQueryable<OpenItemAggregateRow> outstanding = BuildOutstandingQuery(filtered, scope);

        int totalCount = await outstanding.CountAsync(cancellationToken).ConfigureAwait(false);

        int page = request.Page < 1 ? 1 : request.Page;
        int skip = (page - 1) * request.PageSize;

        List<OpenItemAggregateRow> rows = await outstanding
            .Skip(skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<OpenItemDto>
        {
            Items = [.. rows.Select(row => MapOpenItem(Valuate(row, scope, buckets), buckets))],
            TotalCount = totalCount,
            Page = page,
            PageSize = request.PageSize
        };
    }

    /// <summary>
    /// Builds the SQL-side scope of the read: the two inclusion predicates (eligible status, settleable document
    /// type), the as-of issue-date bound, and the optional direction / counterparty / currency / overdue-only
    /// narrowings. Every clause translates to SQL, so no row outside the requested narrowing is ever materialized.
    /// </summary>
    /// <param name="scope">The validated narrowing of the read.</param>
    /// <returns>The deferred, scoped projection query.</returns>
    private IQueryable<InvoiceOpenItem> BuildScopedQuery(AgingScope scope)
    {
        DateTimeOffset dayStart = scope.DayStart;
        DateTimeOffset dayEnd = scope.DayEnd;

        IQueryable<InvoiceOpenItem> items = _db.InvoiceOpenItems
            .AsNoTracking()
            .Where(item => item.InvoiceStatus == ConfirmedInvoiceStatus
                || item.InvoiceStatus == PostedInvoiceStatus)
            .Where(item => SettleableDocumentTypes.Contains(item.DocumentType))
            .Where(item => item.IssueDate < dayEnd);

        if (scope.Direction is not null)
        {
            string direction = scope.Direction;
            items = items.Where(item => item.Direction == direction);
        }

        if (scope.CounterpartyId.HasValue)
        {
            Guid counterpartyId = scope.CounterpartyId.Value;
            items = items.Where(item => item.CounterpartyId == counterpartyId);
        }

        if (scope.CurrencyCode is not null)
        {
            string currencyCode = scope.CurrencyCode;
            items = items.Where(item => item.CurrencyCode == currencyCode);
        }

        if (scope.OverdueOnly)
        {
            items = items.Where(item => item.DueDate < dayStart);
        }

        return items;
    }

    /// <summary>
    /// Projects the scoped items into their as-of settlement shape and drops anything whose outstanding amount is
    /// not strictly positive — a fully settled document is history, not an open item.
    /// <para>The settled amount is resolved by exactly one of two paths, chosen SOLELY by the date: the current day
    /// reads the maintained projection column, while an earlier day sums the invoice's surviving allocation rows
    /// recorded on or before the as-of date whose owning payment is <c>Confirmed</c> or <c>Posted</c>. Both paths
    /// are evaluated server-side, and the outstanding filter is composed onto the projection so the same
    /// expression governs the page count, the page contents, and the report totals.</para>
    /// </summary>
    /// <param name="items">The scoped (and, for the open-item list, already filtered and sorted) query.</param>
    /// <param name="scope">The validated narrowing of the read.</param>
    /// <returns>The deferred query of rows carrying a strictly positive outstanding amount.</returns>
    private IQueryable<OpenItemAggregateRow> BuildOutstandingQuery(
        IQueryable<InvoiceOpenItem> items,
        AgingScope scope)
    {
        return ProjectRows(items, scope)
            .Where(row => row.Item.GrossTotal - row.SettledAsOfDate > 0m);
    }

    private IQueryable<OpenItemAggregateRow> ProjectRows(IQueryable<InvoiceOpenItem> items, AgingScope scope)
    {
        if (!scope.IsHistorical)
        {
            return items.Select(item => new OpenItemAggregateRow
            {
                Item = item,
                SettledAsOfDate = item.SettledAmount
            });
        }

        DateTimeOffset dayEnd = scope.DayEnd;

        return items.Select(item => new OpenItemAggregateRow
        {
            Item = item,
            SettledAsOfDate = _db.PaymentAllocations
                .Where(allocation => allocation.InvoiceId == item.InvoiceId)
                .Where(allocation => allocation.AllocatedAt < dayEnd)
                .Where(allocation => allocation.Payment!.Status == PaymentStatus.Confirmed
                    || allocation.Payment!.Status == PaymentStatus.Posted)
                .Sum(allocation => allocation.AllocatedAmount)
        });
    }

    private OpenItemValuation Valuate(
        OpenItemAggregateRow row,
        AgingScope scope,
        IReadOnlyList<AgingBucketDefinition> buckets)
    {
        decimal outstanding = row.Item.GrossTotal - row.SettledAsOfDate;
        int daysPastDue = AgingBucketCalculator.ComputeDaysPastDue(row.Item.DueDate, scope.AsOfDate);

        return new OpenItemValuation
        {
            Row = row,
            Outstanding = outstanding,
            BaseOutstanding = _countryStrategy.ApplyTaxRounding(outstanding * row.Item.BookingExchangeRate),
            DaysPastDue = daysPastDue,
            BucketIndex = AgingBucketCalculator.Assign(buckets, daysPastDue)
        };
    }

    private OpenItemDto MapOpenItem(OpenItemValuation valuation, IReadOnlyList<AgingBucketDefinition> buckets)
    {
        InvoiceOpenItem item = valuation.Row.Item;
        OpenItemDto dto = _mapper.Map<OpenItemDto>(item);

        return dto with
        {
            SettledAmount = valuation.Row.SettledAsOfDate,
            Outstanding = valuation.Outstanding,
            BaseOutstanding = valuation.BaseOutstanding,
            DaysPastDue = valuation.DaysPastDue,
            AgingBucket = buckets[valuation.BucketIndex].Label,
            SettlementStatus = _settlement.Calculate(valuation.Row.SettledAsOfDate, item.GrossTotal)
        };
    }

    private static IReadOnlyList<CounterpartyAggregate> GroupByCounterparty(
        IReadOnlyList<OpenItemValuation> valuations,
        int bucketCount)
    {
        Dictionary<(Guid CounterpartyId, string CurrencyCode), CounterpartyAggregate> aggregates = [];

        foreach (OpenItemValuation valuation in valuations)
        {
            InvoiceOpenItem item = valuation.Row.Item;
            (Guid CounterpartyId, string CurrencyCode) key = (item.CounterpartyId, item.CurrencyCode);

            if (!aggregates.TryGetValue(key, out CounterpartyAggregate? aggregate))
            {
                aggregate = NewAggregate(item, bucketCount);
                aggregates.Add(key, aggregate);
            }

            aggregate.Apply(valuation);
        }

        List<CounterpartyAggregate> ordered = [.. aggregates.Values];
        ordered.Sort(CompareAggregates);
        return ordered;
    }

    private static CounterpartyAggregate NewAggregate(InvoiceOpenItem item, int bucketCount) => new()
    {
        CounterpartyId = item.CounterpartyId,
        CurrencyCode = item.CurrencyCode,
        BaseCurrencyCode = item.BaseCurrencyCode,
        BucketOutstanding = new decimal[bucketCount],
        BucketBaseOutstanding = new decimal[bucketCount],
        BucketItemCount = new int[bucketCount]
    };

    /// <summary>
    /// Orders grouped rows deterministically: base outstanding descending, then the composite grouping key. A
    /// grouped row has no entity primary key, so the (counterparty, currency) pair IS its final deterministic sort
    /// term (SDD-PAY-003 §2.6, §2.7).
    /// </summary>
    /// <param name="left">The first aggregate.</param>
    /// <param name="right">The second aggregate.</param>
    /// <returns>A negative, zero, or positive ordering value.</returns>
    private static int CompareAggregates(CounterpartyAggregate left, CounterpartyAggregate right)
    {
        int byAmount = right.TotalBaseOutstanding.CompareTo(left.TotalBaseOutstanding);
        if (byAmount != 0)
        {
            return byAmount;
        }

        int byCounterparty = left.CounterpartyId.CompareTo(right.CounterpartyId);
        if (byCounterparty != 0)
        {
            return byCounterparty;
        }

        return string.CompareOrdinal(left.CurrencyCode, right.CurrencyCode);
    }

    private AgingReportDto ComposeReport(
        AgingScope scope,
        IReadOnlyList<AgingBucketDefinition> buckets,
        IReadOnlyList<int> dayBoundaries,
        IReadOnlyList<CounterpartyAggregate> aggregates)
    {
        IReadOnlyList<AgingBucketTotalDto> totals = BuildReportTotals(buckets, aggregates);

        return new AgingReportDto
        {
            AsOfDate = scope.AsOfDate,
            Direction = scope.Direction!,
            BaseCurrencyCode = _countryStrategy.BaseCurrencyCode,
            BucketDayBoundaries = [.. dayBoundaries],
            BucketLabels = [.. buckets.Select(bucket => bucket.Label)],
            Rows = [.. aggregates.Select(aggregate => BuildReportRow(aggregate, buckets))],
            Totals = totals,
            GrandTotalBaseOutstanding = totals.Sum(total => total.BaseOutstanding),
            OpenItemCount = totals.Sum(total => total.ItemCount)
        };
    }

    private static AgingRowDto BuildReportRow(
        CounterpartyAggregate aggregate,
        IReadOnlyList<AgingBucketDefinition> buckets)
    {
        List<AgingBucketAmountDto> amounts = new(buckets.Count);
        for (int index = 0; index < buckets.Count; index++)
        {
            amounts.Add(new AgingBucketAmountDto
            {
                Label = buckets[index].Label,
                FromDaysPastDue = buckets[index].FromDaysPastDue,
                ToDaysPastDue = buckets[index].ToDaysPastDue,
                Outstanding = aggregate.BucketOutstanding[index],
                BaseOutstanding = aggregate.BucketBaseOutstanding[index],
                ItemCount = aggregate.BucketItemCount[index]
            });
        }

        return new AgingRowDto
        {
            CounterpartyId = aggregate.CounterpartyId,
            CurrencyCode = aggregate.CurrencyCode,
            BaseCurrencyCode = aggregate.BaseCurrencyCode,
            OpenItemCount = aggregate.OpenItemCount,
            Buckets = amounts,
            TotalOutstanding = aggregate.TotalOutstanding,
            TotalBaseOutstanding = aggregate.TotalBaseOutstanding
        };
    }

    private static IReadOnlyList<AgingBucketTotalDto> BuildReportTotals(
        IReadOnlyList<AgingBucketDefinition> buckets,
        IReadOnlyList<CounterpartyAggregate> aggregates)
    {
        List<AgingBucketTotalDto> totals = new(buckets.Count);
        for (int index = 0; index < buckets.Count; index++)
        {
            int bucketIndex = index;
            totals.Add(new AgingBucketTotalDto
            {
                Label = buckets[bucketIndex].Label,
                FromDaysPastDue = buckets[bucketIndex].FromDaysPastDue,
                ToDaysPastDue = buckets[bucketIndex].ToDaysPastDue,
                BaseOutstanding = aggregates.Sum(aggregate => aggregate.BucketBaseOutstanding[bucketIndex]),
                ItemCount = aggregates.Sum(aggregate => aggregate.BucketItemCount[bucketIndex])
            });
        }

        return totals;
    }

    /// <summary>
    /// Pages the shared aggregates in memory. The grouped rows are not entities, so the SDD-INFRA-005 attribute
    /// pipeline cannot order them; the ordering SDD-PAY-003 §2.7 pins (base outstanding descending, then the
    /// composite grouping key) is applied instead and the page-size cap is still enforced. A zero-outstanding pair
    /// cannot occur, because every item contributing to a pair has a strictly positive outstanding amount.
    /// </summary>
    /// <param name="scope">The validated narrowing of the read, supplying the reported direction.</param>
    /// <param name="aggregates">The already-ordered shared aggregates.</param>
    /// <param name="request">The pagination request.</param>
    /// <returns>The page of counterparty balances.</returns>
    private PagedResult<CounterpartyBalanceDto> ComposeBalances(
        AgingScope scope,
        IReadOnlyList<CounterpartyAggregate> aggregates,
        FilterRequest request)
    {
        int page = request.Page < 1 ? 1 : request.Page;
        int skip = (page - 1) * request.PageSize;

        List<CounterpartyBalanceDto> items =
        [
            .. aggregates
                .Skip(skip)
                .Take(request.PageSize)
                .Select(aggregate => BuildBalance(aggregate, scope.Direction!))
        ];

        return new PagedResult<CounterpartyBalanceDto>
        {
            Items = items,
            TotalCount = aggregates.Count,
            Page = page,
            PageSize = request.PageSize
        };
    }

    private static CounterpartyBalanceDto BuildBalance(CounterpartyAggregate aggregate, string direction) => new()
    {
        CounterpartyId = aggregate.CounterpartyId,
        CurrencyCode = aggregate.CurrencyCode,
        BaseCurrencyCode = aggregate.BaseCurrencyCode,
        Direction = direction,
        OpenItemCount = aggregate.OpenItemCount,
        Outstanding = aggregate.TotalOutstanding,
        BaseOutstanding = aggregate.TotalBaseOutstanding,
        OverdueOutstanding = aggregate.OverdueOutstanding,
        BaseOverdueOutstanding = aggregate.BaseOverdueOutstanding,
        OldestDueDate = aggregate.OldestDueDate
    };

    private AgingScope BuildScope(OpenItemQueryRequest query) => NewScope(query.AsOfDate) with
    {
        Direction = query.Direction,
        CounterpartyId = query.CounterpartyId,
        CurrencyCode = query.CurrencyCode,
        OverdueOnly = query.OverdueOnly
    };

    private AgingScope BuildScope(AgingReportQueryRequest query) => NewScope(query.AsOfDate) with
    {
        Direction = query.Direction,
        CounterpartyId = query.CounterpartyId,
        CurrencyCode = query.CurrencyCode
    };

    private AgingScope BuildScope(CounterpartyBalanceQueryRequest query) => NewScope(query.AsOfDate) with
    {
        Direction = query.Direction,
        CurrencyCode = query.CurrencyCode
    };

    /// <summary>
    /// Seeds a scope with the effective as-of date and the clock's current day. An omitted as-of date defaults to
    /// the current date, which is the only default the aging surface has.
    /// </summary>
    /// <param name="asOfDate">The caller's as-of date, already validated as not being in the future.</param>
    /// <returns>The seeded scope, ready for the narrowings to be applied.</returns>
    private AgingScope NewScope(DateTimeOffset? asOfDate)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        return new AgingScope
        {
            AsOfDate = asOfDate ?? now,
            Today = DateOnly.FromDateTime(now.UtcDateTime)
        };
    }

    private Result ValidateOpenItemQuery(OpenItemQueryRequest query, FilterRequest request)
    {
        if (query.AsOfDate.HasValue && !AgingQueryRules.IsNotInFuture(query.AsOfDate.Value, _timeProvider))
        {
            return FutureAsOfDateFailure();
        }

        if (query.Direction is not null && !AgingQueryRules.IsRecognizedDirection(query.Direction))
        {
            return UnrecognizedDirectionFailure();
        }

        Result counterparty = ValidateCounterparty(query.CounterpartyId);
        if (!counterparty.IsSuccess)
        {
            return counterparty;
        }

        Result currency = ValidateCurrency(query.CurrencyCode);
        if (!currency.IsSuccess)
        {
            return currency;
        }

        return ValidatePageSize(request.PageSize);
    }

    private Result ValidateReportQuery(AgingReportQueryRequest query)
    {
        Result required = ValidateRequiredAsOfDate(query.AsOfDate);
        if (!required.IsSuccess)
        {
            return required;
        }

        if (!AgingQueryRules.IsRecognizedDirection(query.Direction))
        {
            return UnrecognizedDirectionFailure();
        }

        Result counterparty = ValidateCounterparty(query.CounterpartyId);
        if (!counterparty.IsSuccess)
        {
            return counterparty;
        }

        return ValidateCurrency(query.CurrencyCode);
    }

    private Result ValidateBalanceQuery(CounterpartyBalanceQueryRequest query, FilterRequest request)
    {
        Result required = ValidateRequiredAsOfDate(query.AsOfDate);
        if (!required.IsSuccess)
        {
            return required;
        }

        if (!AgingQueryRules.IsRecognizedDirection(query.Direction))
        {
            return UnrecognizedDirectionFailure();
        }

        Result currency = ValidateCurrency(query.CurrencyCode);
        if (!currency.IsSuccess)
        {
            return currency;
        }

        return ValidatePageSize(request.PageSize);
    }

    private Result ValidateRequiredAsOfDate(DateTimeOffset? asOfDate)
    {
        if (!asOfDate.HasValue)
        {
            return Result.Failure(
                PaymentErrorCodes.INVALID_AGING_AS_OF_DATE,
                "asOfDate is required on the aging report endpoints.");
        }

        if (!AgingQueryRules.IsNotInFuture(asOfDate.Value, _timeProvider))
        {
            return FutureAsOfDateFailure();
        }

        return Result.Success();
    }

    private static Result ValidateCounterparty(Guid? counterpartyId)
    {
        if (counterpartyId.HasValue && counterpartyId.Value == Guid.Empty)
        {
            return Result.Failure(
                PaymentErrorCodes.INVALID_COUNTERPARTY_ID,
                "counterpartyId must be a non-empty GUID when supplied.");
        }

        return Result.Success();
    }

    private static Result ValidateCurrency(string? currencyCode)
    {
        if (currencyCode is not null && !AgingQueryRules.IsWellFormedCurrency(currencyCode))
        {
            return Result.Failure(
                PaymentErrorCodes.INVALID_AGING_CURRENCY,
                "currencyCode must be a three-letter ISO 4217 code when supplied.");
        }

        return Result.Success();
    }

    private static Result ValidatePageSize(int pageSize)
    {
        if (pageSize > QueryableFilterExtensions.MaxPageSize)
        {
            return Result.Failure(
                FilterErrorCodes.PAGE_SIZE_TOO_LARGE,
                $"The requested page size {pageSize} exceeds the maximum of {QueryableFilterExtensions.MaxPageSize}.");
        }

        return Result.Success();
    }

    private static Result FutureAsOfDateFailure() => Result.Failure(
        PaymentErrorCodes.INVALID_AGING_AS_OF_DATE,
        "asOfDate must not be in the future.");

    private static Result UnrecognizedDirectionFailure() => Result.Failure(
        PaymentErrorCodes.INVALID_AGING_DIRECTION,
        "direction must be either 'AR' or 'AP'.");

    /// <summary>
    /// Applies the documented default ordering (oldest due date first) when the caller supplies none. The
    /// SDD-INFRA-005 pipeline always appends the projection primary key as the final sort term, so pagination stays
    /// deterministic (SDD-PAY-003 §2.5).
    /// </summary>
    /// <param name="request">The caller's filter request.</param>
    /// <returns>The request, with the default sort applied when it carried none.</returns>
    private static FilterRequest ApplyDefaultSort(FilterRequest request)
    {
        if (request.Sort.Count > 0)
        {
            return request;
        }

        return request with
        {
            Sort = [new SortCriterion { Field = DueDateSortField, Direction = AscendingSortDirection }]
        };
    }

    /// <summary>
    /// Derives the aged population from the SHARED <see cref="SettlementPairing"/> table rather than re-listing a
    /// document-type set locally, so the aged population and the allocatable population cannot drift. The projection
    /// stores the document type as its enum member NAME, so the names are what the query compares.
    /// </summary>
    /// <returns>The names of the invoice document types some payment document type can settle.</returns>
    private static IReadOnlyList<string> BuildSettleableDocumentTypes()
    {
        return new List<string>(
            Enum.GetValues<InvoiceDocumentType>()
                .Where(SettlementPairing.IsSettleableInvoiceType)
                .Select(documentType => documentType.ToString()));
    }
}
