using System.Text.Json;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Finance.Common.Abstractions;
using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Common.Workflow;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Infrastructure.Audit.Models;
using Finance.Infrastructure.Caching.Interfaces;
using Finance.Infrastructure.Services;
using Finance.Periods.API.Auditing;
using Finance.Periods.API.Caching;
using Finance.Periods.API.Interfaces;
using Finance.Periods.API.Models;
using Finance.Periods.DBModel;
using Finance.Periods.DBModel.Models;
using Finance.ServiceModel.Events.Periods;
using Finance.ServiceModel.Periods;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Finance.Periods.API.Services;

/// <summary>
/// Default <see cref="IFiscalPeriodService"/> built on <see cref="SearchableServiceBase{TEntity, TDto, TContext}"/>
/// (SDD-FIN-004, SDD-INFRA-009). Close and reopen run through <see cref="IWorkflowEngine{TAggregate}"/>,
/// write an audit row, and publish a domain event via the transactional outbox — all inside one
/// transaction. Period status is cached as reference data and invalidated on every write.
/// </summary>
public sealed class FiscalPeriodService
    : SearchableServiceBase<FiscalPeriod, FiscalPeriodDto, PeriodsDbContext>, IFiscalPeriodService
{
    private readonly IFiscalCalendar _calendar;
    private readonly IWorkflowEngine<FiscalPeriod> _workflow;
    private readonly IAuditService _audit;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ICacheService<FiscalPeriodDto> _periodCache;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Creates a new <see cref="FiscalPeriodService"/>.</summary>
    /// <param name="db">The periods database context.</param>
    /// <param name="mapper">The AutoMapper instance used for projection.</param>
    /// <param name="correlation">The ambient correlation-id accessor.</param>
    /// <param name="calendar">The fiscal-calendar seam used to derive periods (SDD-CTRY-001 seam).</param>
    /// <param name="workflow">The fiscal-period workflow engine (SDD-INFRA-008).</param>
    /// <param name="audit">The write-path audit service (SDD-AUDIT-001).</param>
    /// <param name="publishEndpoint">The transactional-outbox publish endpoint (SDD-INFRA-006).</param>
    /// <param name="periodCache">The reference-read cache for period status (SDD-INFRA-004).</param>
    /// <param name="currentUser">The authenticated-user accessor used to stamp audit rows and closes.</param>
    public FiscalPeriodService(
        PeriodsDbContext db,
        IMapper mapper,
        ICorrelationIdAccessor correlation,
        IFiscalCalendar calendar,
        IWorkflowEngine<FiscalPeriod> workflow,
        IAuditService audit,
        IPublishEndpoint publishEndpoint,
        ICacheService<FiscalPeriodDto> periodCache,
        ICurrentUserAccessor currentUser)
        : base(db, mapper, correlation)
    {
        _calendar = calendar;
        _workflow = workflow;
        _audit = audit;
        _publishEndpoint = publishEndpoint;
        _periodCache = periodCache;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public new Task<Result<PagedResult<FiscalPeriodDto>>> SearchAsync(
        FilterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return base.SearchAsync(ApplyDefaultSort(request), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<FiscalPeriodDto>> GetAsync(int id, CancellationToken cancellationToken)
    {
        FiscalPeriodDto? dto = await LoadDtoByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (dto is null)
        {
            return Result<FiscalPeriodDto>.Failure(PeriodErrorCodes.PERIOD_NOT_FOUND);
        }

        return Result<FiscalPeriodDto>.Success(dto);
    }

    /// <inheritdoc />
    public async Task<Result<FiscalPeriodDto>> GetByDateAsync(DateTimeOffset date, CancellationToken cancellationToken)
    {
        FiscalPeriodDto? dto = await _periodCache.GetOrSetAsync(
            PeriodCacheKeys.ByDate(date),
            ct => LoadContainingPeriodAsync(date, ct),
            ttl: null,
            cancellationToken).ConfigureAwait(false);

        if (dto is null)
        {
            return Result<FiscalPeriodDto>.Failure(PeriodErrorCodes.NO_PERIOD_FOR_DATE);
        }

        return Result<FiscalPeriodDto>.Success(dto);
    }

    /// <inheritdoc />
    public async Task<Result<FiscalPeriodDto>> GetByYearNumberAsync(
        int fiscalYear,
        int periodNumber,
        CancellationToken cancellationToken)
    {
        FiscalPeriodDto? dto = await Db.FiscalPeriods
            .AsNoTracking()
            .Where(period => period.FiscalYear == fiscalYear && period.PeriodNumber == periodNumber)
            .ProjectTo<FiscalPeriodDto>(Mapper.ConfigurationProvider)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (dto is null)
        {
            return Result<FiscalPeriodDto>.Failure(PeriodErrorCodes.PERIOD_NOT_FOUND);
        }

        return Result<FiscalPeriodDto>.Success(dto);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<FiscalPeriodDto>>> GenerateAsync(
        GeneratePeriodsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        bool yearExists = await Db.FiscalPeriods
            .AsNoTracking()
            .AnyAsync(period => period.FiscalYear == request.FiscalYear, cancellationToken)
            .ConfigureAwait(false);
        if (yearExists)
        {
            return Result<IReadOnlyList<FiscalPeriodDto>>.Failure(PeriodErrorCodes.DUPLICATE_PERIOD);
        }

        IReadOnlyList<FiscalPeriod> periods = BuildPeriods(request.FiscalYear);
        return await PersistGenerateAsync(periods, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<FiscalPeriodDto>> CreateAsync(
        CreatePeriodRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result uniqueness = await EnsureCreatableAsync(request, cancellationToken).ConfigureAwait(false);
        if (!uniqueness.IsSuccess)
        {
            return Result<FiscalPeriodDto>.Failure(uniqueness.ErrorCode!, uniqueness.Detail);
        }

        FiscalPeriod period = BuildPeriod(
            request.FiscalYear,
            request.PeriodNumber,
            ResolveName(request),
            request.StartDate,
            request.EndDate);

        return await PersistCreateAsync(period, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<FiscalPeriodDto>> CloseAsync(
        int id,
        ClosePeriodRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result<FiscalPeriodDto>.Failure(PeriodErrorCodes.CLOSE_REASON_REQUIRED);
        }

        FiscalPeriod? period = await LoadTrackedAsync(id, cancellationToken).ConfigureAwait(false);
        if (period is null)
        {
            return Result<FiscalPeriodDto>.Failure(PeriodErrorCodes.PERIOD_NOT_FOUND);
        }

        if (period.Status == FiscalPeriodStatus.Closed)
        {
            return Result<FiscalPeriodDto>.Failure(PeriodErrorCodes.PERIOD_ALREADY_CLOSED);
        }

        Result tokenResult = ApplyConcurrencyToken(period, request.RowVersion);
        if (!tokenResult.IsSuccess)
        {
            return Result<FiscalPeriodDto>.Failure(tokenResult.ErrorCode!, tokenResult.Detail);
        }

        return await CloseInTransactionAsync(period, request.Reason, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<FiscalPeriodDto>> ReopenAsync(
        int id,
        ReopenPeriodRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result<FiscalPeriodDto>.Failure(PeriodErrorCodes.REOPEN_REASON_REQUIRED);
        }

        FiscalPeriod? period = await LoadTrackedAsync(id, cancellationToken).ConfigureAwait(false);
        if (period is null)
        {
            return Result<FiscalPeriodDto>.Failure(PeriodErrorCodes.PERIOD_NOT_FOUND);
        }

        if (period.Status == FiscalPeriodStatus.Open)
        {
            return Result<FiscalPeriodDto>.Failure(PeriodErrorCodes.PERIOD_ALREADY_OPEN);
        }

        Result tokenResult = ApplyConcurrencyToken(period, request.RowVersion);
        if (!tokenResult.IsSuccess)
        {
            return Result<FiscalPeriodDto>.Failure(tokenResult.ErrorCode!, tokenResult.Detail);
        }

        return await ReopenInTransactionAsync(period, request.Reason, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Default-orders the list by descending <c>FiscalYear</c> then ascending <c>PeriodNumber</c>.</summary>
    /// <returns>A non-tracking ordered query over the period set.</returns>
    protected override IQueryable<FiscalPeriod> BuildBaseQuery()
    {
        return base.BuildBaseQuery()
            .OrderByDescending(period => period.FiscalYear)
            .ThenBy(period => period.PeriodNumber);
    }

    private IReadOnlyList<FiscalPeriod> BuildPeriods(int fiscalYear)
    {
        IReadOnlyList<PeriodDescriptor> descriptors = _calendar.GeneratePeriods(fiscalYear);
        List<FiscalPeriod> periods = new(descriptors.Count);
        foreach (PeriodDescriptor descriptor in descriptors)
        {
            periods.Add(BuildPeriod(
                fiscalYear,
                descriptor.PeriodNumber,
                descriptor.Name,
                descriptor.StartDate,
                descriptor.EndDate));
        }

        return periods;
    }

    private FiscalPeriod BuildPeriod(
        int fiscalYear,
        int periodNumber,
        string name,
        DateTimeOffset startDate,
        DateTimeOffset endDate)
    {
        return new FiscalPeriod
        {
            FiscalYear = fiscalYear,
            PeriodNumber = periodNumber,
            Name = name,
            StartDate = startDate,
            EndDate = endDate,
            Status = FiscalPeriodStatus.Open,
            CorrelationId = Correlation.Get(),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = _currentUser.GetUserId()
        };
    }

    private async Task<Result> EnsureCreatableAsync(CreatePeriodRequest request, CancellationToken cancellationToken)
    {
        bool duplicate = await Db.FiscalPeriods
            .AsNoTracking()
            .AnyAsync(
                period => period.FiscalYear == request.FiscalYear && period.PeriodNumber == request.PeriodNumber,
                cancellationToken)
            .ConfigureAwait(false);
        if (duplicate)
        {
            return Result.Failure(PeriodErrorCodes.DUPLICATE_PERIOD);
        }

        bool overlaps = await Db.FiscalPeriods
            .AsNoTracking()
            .AnyAsync(
                period => period.StartDate <= request.EndDate && request.StartDate <= period.EndDate,
                cancellationToken)
            .ConfigureAwait(false);
        if (overlaps)
        {
            return Result.Failure(PeriodErrorCodes.OVERLAPPING_PERIOD);
        }

        return Result.Success();
    }

    private async Task<Result<IReadOnlyList<FiscalPeriodDto>>> PersistGenerateAsync(
        IReadOnlyList<FiscalPeriod> periods,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        Db.FiscalPeriods.AddRange(periods);
        Result inserted = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!inserted.IsSuccess)
        {
            return Result<IReadOnlyList<FiscalPeriodDto>>.Failure(inserted.ErrorCode!, inserted.Detail);
        }

        foreach (FiscalPeriod period in periods)
        {
            Result audited = await RecordCreateAuditAsync(period, cancellationToken).ConfigureAwait(false);
            if (!audited.IsSuccess)
            {
                return Result<IReadOnlyList<FiscalPeriodDto>>.Failure(audited.ErrorCode!, audited.Detail);
            }
        }

        Result flushed = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!flushed.IsSuccess)
        {
            return Result<IReadOnlyList<FiscalPeriodDto>>.Failure(flushed.ErrorCode!, flushed.Detail);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateRegionAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<FiscalPeriodDto> dtos = [.. periods.Select(Mapper.Map<FiscalPeriodDto>)];
        return Result<IReadOnlyList<FiscalPeriodDto>>.Success(dtos);
    }

    private async Task<Result<FiscalPeriodDto>> PersistCreateAsync(
        FiscalPeriod period,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        Db.FiscalPeriods.Add(period);
        Result inserted = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!inserted.IsSuccess)
        {
            return Result<FiscalPeriodDto>.Failure(inserted.ErrorCode!, inserted.Detail);
        }

        Result audited = await RecordCreateAuditAsync(period, cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<FiscalPeriodDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        Result flushed = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!flushed.IsSuccess)
        {
            return Result<FiscalPeriodDto>.Failure(flushed.ErrorCode!, flushed.Detail);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateRegionAsync(cancellationToken).ConfigureAwait(false);
        return Result<FiscalPeriodDto>.Success(Mapper.Map<FiscalPeriodDto>(period));
    }

    private async Task<Result<FiscalPeriodDto>> CloseInTransactionAsync(
        FiscalPeriod period,
        string reason,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        string beforeJson = SerializePeriod(period);

        Result transition = await TransitionAsync(
            period, FiscalPeriodStatus.Closed, reason, cancellationToken).ConfigureAwait(false);
        if (!transition.IsSuccess)
        {
            return Result<FiscalPeriodDto>.Failure(transition.ErrorCode!, transition.Detail);
        }

        period.Status = FiscalPeriodStatus.Closed;
        period.ClosedAt = DateTimeOffset.UtcNow;
        period.ClosedBy = _currentUser.GetUserId();
        AppendStatusHistory(period, FiscalPeriodStatus.Open, FiscalPeriodStatus.Closed, reason);

        Result audited = await RecordStateChangeAuditAsync(
            PeriodAuditEventTypes.FiscalPeriodClosed, period, beforeJson, reason, cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<FiscalPeriodDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        await _publishEndpoint.Publish(BuildClosedEvent(period, reason), cancellationToken).ConfigureAwait(false);

        return await CommitTransitionAsync(transaction, period, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<FiscalPeriodDto>> ReopenInTransactionAsync(
        FiscalPeriod period,
        string reason,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        string beforeJson = SerializePeriod(period);

        Result transition = await TransitionAsync(
            period, FiscalPeriodStatus.Open, reason, cancellationToken).ConfigureAwait(false);
        if (!transition.IsSuccess)
        {
            return Result<FiscalPeriodDto>.Failure(transition.ErrorCode!, transition.Detail);
        }

        period.Status = FiscalPeriodStatus.Open;
        period.ReopenedAt = DateTimeOffset.UtcNow;
        period.ReopenedBy = _currentUser.GetUserId();
        AppendStatusHistory(period, FiscalPeriodStatus.Closed, FiscalPeriodStatus.Open, reason);

        Result audited = await RecordStateChangeAuditAsync(
            PeriodAuditEventTypes.FiscalPeriodReopened, period, beforeJson, reason, cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<FiscalPeriodDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        await _publishEndpoint.Publish(BuildReopenedEvent(period, reason), cancellationToken).ConfigureAwait(false);

        return await CommitTransitionAsync(transaction, period, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<FiscalPeriodDto>> CommitTransitionAsync(
        IDbContextTransaction transaction,
        FiscalPeriod period,
        CancellationToken cancellationToken)
    {
        Result saved = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            return Result<FiscalPeriodDto>.Failure(saved.ErrorCode!, saved.Detail);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateRegionAsync(cancellationToken).ConfigureAwait(false);
        return Result<FiscalPeriodDto>.Success(Mapper.Map<FiscalPeriodDto>(period));
    }

    private async Task<Result> TransitionAsync(
        FiscalPeriod period,
        FiscalPeriodStatus target,
        string reason,
        CancellationToken cancellationToken)
    {
        WorkflowContext<FiscalPeriod> context = new()
        {
            Aggregate = period,
            CurrentState = period.Status.ToString(),
            TargetState = target.ToString(),
            Reason = reason,
            CorrelationId = Correlation.Get()
        };

        Result transition = await _workflow.TransitionAsync(context, cancellationToken).ConfigureAwait(false);
        if (!transition.IsSuccess)
        {
            return Result.Failure(TranslateTransitionCode(transition), transition.Detail);
        }

        return Result.Success();
    }

    private static string TranslateTransitionCode(Result transition)
    {
        if (transition.ErrorCode == WorkflowErrorCodes.WORKFLOW_GUARD_FAILED && transition.Detail is not null)
        {
            return transition.Detail;
        }

        if (transition.ErrorCode == WorkflowErrorCodes.INVALID_STATE_TRANSITION ||
            transition.ErrorCode == WorkflowErrorCodes.STATE_NOT_REGISTERED)
        {
            return PeriodErrorCodes.INVALID_PERIOD_STATE_TRANSITION;
        }

        return transition.ErrorCode!;
    }

    private void AppendStatusHistory(
        FiscalPeriod period,
        FiscalPeriodStatus fromStatus,
        FiscalPeriodStatus toStatus,
        string reason)
    {
        period.StatusHistory.Add(new FiscalPeriodStatusHistory
        {
            FromStatus = fromStatus.ToString(),
            ToStatus = toStatus.ToString(),
            ChangedBy = _currentUser.GetUserId(),
            ChangedAt = DateTimeOffset.UtcNow,
            CorrelationId = Correlation.Get(),
            Reason = reason
        });
    }

    private Task<Result> RecordCreateAuditAsync(FiscalPeriod period, CancellationToken cancellationToken)
    {
        AuditEntry entry = new()
        {
            EventType = PeriodAuditEventTypes.FiscalPeriodCreated,
            Operation = AuditOperation.Create,
            EntityType = PeriodAuditEventTypes.EntityType,
            EntityId = period.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            UserId = _currentUser.GetUserId(),
            Username = _currentUser.GetUsername(),
            OccurredAt = DateTimeOffset.UtcNow,
            CorrelationId = Correlation.Get(),
            BeforeJson = null,
            AfterJson = SerializePeriod(period),
            Reason = null
        };

        return _audit.RecordAsync(entry, cancellationToken);
    }

    private Task<Result> RecordStateChangeAuditAsync(
        string eventType,
        FiscalPeriod period,
        string beforeJson,
        string reason,
        CancellationToken cancellationToken)
    {
        AuditEntry entry = new()
        {
            EventType = eventType,
            Operation = AuditOperation.StateChange,
            EntityType = PeriodAuditEventTypes.EntityType,
            EntityId = period.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            UserId = _currentUser.GetUserId(),
            Username = _currentUser.GetUsername(),
            OccurredAt = DateTimeOffset.UtcNow,
            CorrelationId = Correlation.Get(),
            BeforeJson = beforeJson,
            AfterJson = SerializePeriod(period),
            Reason = reason
        };

        return _audit.RecordAsync(entry, cancellationToken);
    }

    private FiscalPeriodClosedEvent BuildClosedEvent(FiscalPeriod period, string reason) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = Correlation.Get(),
        OccurredAt = DateTimeOffset.UtcNow,
        FiscalPeriodId = period.Id,
        FiscalYear = period.FiscalYear,
        PeriodNumber = period.PeriodNumber,
        StartDate = period.StartDate,
        EndDate = period.EndDate,
        Reason = reason
    };

    private FiscalPeriodReopenedEvent BuildReopenedEvent(FiscalPeriod period, string reason) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = Correlation.Get(),
        OccurredAt = DateTimeOffset.UtcNow,
        FiscalPeriodId = period.Id,
        FiscalYear = period.FiscalYear,
        PeriodNumber = period.PeriodNumber,
        StartDate = period.StartDate,
        EndDate = period.EndDate,
        Reason = reason
    };

    private Task<FiscalPeriod?> LoadTrackedAsync(int id, CancellationToken cancellationToken)
    {
        return Db.FiscalPeriods
            .Include(period => period.StatusHistory)
            .FirstOrDefaultAsync(period => period.Id == id, cancellationToken);
    }

    private Task<FiscalPeriodDto?> LoadDtoByIdAsync(int id, CancellationToken cancellationToken)
    {
        return Db.FiscalPeriods
            .AsNoTracking()
            .Where(period => period.Id == id)
            .ProjectTo<FiscalPeriodDto>(Mapper.ConfigurationProvider)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private Task<FiscalPeriodDto?> LoadContainingPeriodAsync(DateTimeOffset date, CancellationToken cancellationToken)
    {
        return Db.FiscalPeriods
            .AsNoTracking()
            .Where(period => period.StartDate <= date && date <= period.EndDate)
            .ProjectTo<FiscalPeriodDto>(Mapper.ConfigurationProvider)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private Task InvalidateRegionAsync(CancellationToken cancellationToken)
    {
        return _periodCache.RemoveByPatternAsync(PeriodCacheKeys.InvalidationPattern, cancellationToken);
    }

    private Result ApplyConcurrencyToken(FiscalPeriod period, string rowVersion)
    {
        if (!TryDecodeRowVersion(rowVersion, out byte[] originalRowVersion))
        {
            return Result.Failure(
                CommonErrorCodes.CONCURRENT_MODIFICATION,
                "The supplied row version is not a valid base64 token.");
        }

        Db.Entry(period).Property(p => p.RowVersion).OriginalValue = originalRowVersion;
        return Result.Success();
    }

    private static bool TryDecodeRowVersion(string rowVersion, out byte[] decoded)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
        {
            decoded = [];
            return false;
        }

        try
        {
            decoded = Convert.FromBase64String(rowVersion);
            return true;
        }
        catch (FormatException)
        {
            decoded = [];
            return false;
        }
    }

    private static string ResolveName(CreatePeriodRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            return request.Name;
        }

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Period {request.PeriodNumber} {request.FiscalYear}");
    }

    private static string SerializePeriod(FiscalPeriod period)
    {
        return JsonSerializer.Serialize(new
        {
            period.Id,
            period.FiscalYear,
            period.PeriodNumber,
            period.Name,
            period.StartDate,
            period.EndDate,
            Status = period.Status.ToString(),
            period.ClosedAt,
            period.ReopenedAt
        });
    }

    private static FilterRequest ApplyDefaultSort(FilterRequest request)
    {
        if (request.Sort.Count > 0)
        {
            return request;
        }

        return request with
        {
            Sort =
            [
                new SortCriterion { Field = nameof(FiscalPeriod.FiscalYear), Direction = "desc" },
                new SortCriterion { Field = nameof(FiscalPeriod.PeriodNumber), Direction = "asc" }
            ]
        };
    }
}
