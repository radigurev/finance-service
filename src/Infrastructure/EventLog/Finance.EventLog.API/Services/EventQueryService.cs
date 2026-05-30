using System.Globalization;
using AutoMapper;
using Finance.Common.Abstractions;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.EventLog.API.Interfaces;
using Finance.EventLog.DBModel;
using Finance.EventLog.DBModel.Models;
using Finance.GenericFiltering.Models;
using Finance.ServiceModel.EventLog;

namespace Finance.EventLog.API.Services;

/// <summary>
/// Default <see cref="IEventQueryService"/> implementation built on the shared
/// <see cref="Finance.Infrastructure.Services.SearchableServiceBase{TEntity, TDto, TContext}"/>. It applies
/// a default <c>OccurredAt</c>-descending sort, validates the optional date range, and folds an optional
/// <c>correlationId</c> shortcut into the filter request (SDD-EVTLOG-001 §2.4-§2.5). The query is read-only:
/// EventLog rows are append-only and never mutated here.
/// </summary>
public sealed class EventQueryService
    : Finance.Infrastructure.Services.SearchableServiceBase<EventLogEntry, EventLogEntryDto, EventLogDbContext>,
        IEventQueryService
{
    private const string OccurredAtField = nameof(EventLogEntry.OccurredAt);
    private const string CorrelationIdField = nameof(EventLogEntry.CorrelationId);
    private const string DescendingDirection = "desc";

    /// <summary>Creates a new <see cref="EventQueryService"/>.</summary>
    /// <param name="db">The EventLog database context.</param>
    /// <param name="mapper">The AutoMapper instance used for projection.</param>
    /// <param name="correlation">The ambient correlation-id accessor.</param>
    public EventQueryService(EventLogDbContext db, IMapper mapper, ICorrelationIdAccessor correlation)
        : base(db, mapper, correlation)
    {
    }

    /// <inheritdoc />
    public Task<Result<PagedResult<EventLogEntryDto>>> SearchAsync(
        FilterRequest request,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result rangeResult = ValidateDateRange(request);
        if (!rangeResult.IsSuccess)
        {
            return Task.FromResult(
                Result<PagedResult<EventLogEntryDto>>.Failure(rangeResult.ErrorCode!, rangeResult.Detail));
        }

        FilterRequest effectiveRequest = ApplyCorrelationFilter(request, correlationId);
        effectiveRequest = ApplyDefaultSort(effectiveRequest);
        return base.SearchAsync(effectiveRequest, cancellationToken);
    }

    /// <summary>
    /// Builds the base, non-tracking query the search starts from (SDD-EVTLOG-001 §2.4). The default
    /// <c>OccurredAt</c>-descending order is injected as a sort term in <see cref="ApplyDefaultSort"/> so the
    /// SDD-INFRA-005 filtering library can append the primary key as the final deterministic term.
    /// </summary>
    /// <returns>A non-tracking query over the event-log archive.</returns>
    protected override IQueryable<EventLogEntry> BuildBaseQuery()
    {
        return base.BuildBaseQuery();
    }

    private static FilterRequest ApplyDefaultSort(FilterRequest request)
    {
        if (request.Sort.Count > 0)
        {
            return request;
        }

        return request with
        {
            Sort = [new SortCriterion { Field = OccurredAtField, Direction = DescendingDirection }]
        };
    }

    private static FilterRequest ApplyCorrelationFilter(FilterRequest request, string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return request;
        }

        List<FilterCriterion> filters = [.. request.Filters];
        filters.Add(new FilterCriterion
        {
            Field = CorrelationIdField,
            Operator = "eq",
            Value = correlationId
        });

        return request with { Filters = filters };
    }

    private static Result ValidateDateRange(FilterRequest request)
    {
        DateTimeOffset? from = null;
        DateTimeOffset? to = null;

        foreach (FilterCriterion criterion in request.Filters)
        {
            if (!string.Equals(criterion.Field, OccurredAtField, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ApplyRangeBound(criterion, ref from, ref to);
        }

        if (from is not null && to is not null && from > to)
        {
            return Result.Failure(
                EventLogErrorCodes.INVALID_DATE_RANGE,
                "The supplied 'from' date is after the 'to' date.");
        }

        return Result.Success();
    }

    private static void ApplyRangeBound(
        FilterCriterion criterion,
        ref DateTimeOffset? from,
        ref DateTimeOffset? to)
    {
        switch (criterion.Operator?.ToLowerInvariant())
        {
            case "gt":
            case "gte":
                from = ParseBound(criterion.Value) ?? from;
                break;
            case "lt":
            case "lte":
                to = ParseBound(criterion.Value) ?? to;
                break;
            case "between":
                ApplyBetweenBounds(criterion.Value, ref from, ref to);
                break;
        }
    }

    private static void ApplyBetweenBounds(object? value, ref DateTimeOffset? from, ref DateTimeOffset? to)
    {
        if (value is not IEnumerable<object?> values)
        {
            return;
        }

        List<object?> items = [.. values];
        if (items.Count >= 1)
        {
            from = ParseBound(items[0]) ?? from;
        }

        if (items.Count >= 2)
        {
            to = ParseBound(items[1]) ?? to;
        }
    }

    private static DateTimeOffset? ParseBound(object? value)
    {
        if (value is DateTimeOffset offset)
        {
            return offset;
        }

        if (value is DateTime dateTime)
        {
            return new DateTimeOffset(dateTime);
        }

        if (value is string text
            && DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed))
        {
            return parsed;
        }

        return null;
    }
}
