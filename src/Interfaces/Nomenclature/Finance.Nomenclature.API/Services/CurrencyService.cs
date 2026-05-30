using System.Globalization;
using System.Text.Json;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Finance.Common.Abstractions;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Common.Validation;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Infrastructure.Audit.Models;
using Finance.Infrastructure.Caching.Interfaces;
using Finance.Infrastructure.Services;
using Finance.Nomenclature.API.Auditing;
using Finance.Nomenclature.API.Caching;
using Finance.Nomenclature.API.Interfaces;
using Finance.Nomenclature.DBModel;
using Finance.Nomenclature.DBModel.Models;
using Finance.ServiceModel.Events.Nomenclature;
using Finance.ServiceModel.Nomenclature;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Finance.Nomenclature.API.Services;

/// <summary>
/// Default <see cref="ICurrencyService"/> implementation built on the shared
/// <see cref="SearchableServiceBase{TEntity, TDto, TContext}"/> / <see cref="BaseEntityService{TContext}"/>
/// helpers. Every public method returns a <see cref="Result"/> / <see cref="Result{T}"/>
/// (SDD-NOM-001 §2.1, SDD-INFRA-009). Writes record an audit row, publish a domain event via the
/// transactional outbox, and invalidate the reference-read cache region (SDD-AUDIT-001, SDD-INFRA-006,
/// SDD-INFRA-004).
/// </summary>
public sealed class CurrencyService
    : SearchableServiceBase<Currency, CurrencyDto, NomenclatureDbContext>, ICurrencyService
{
    private const string IsoCodeSortField = nameof(Currency.IsoCode);

    private readonly ValidationChain<CreateCurrencyRequest> _createChain;
    private readonly IAuditService _audit;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ICacheService<IReadOnlyList<CurrencyDto>> _currencyListCache;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Creates a new <see cref="CurrencyService"/>.</summary>
    /// <param name="db">The nomenclature database context.</param>
    /// <param name="mapper">The AutoMapper instance used for projection.</param>
    /// <param name="correlation">The ambient correlation-id accessor.</param>
    /// <param name="createChain">The cross-aggregate validation chain for currency creation.</param>
    /// <param name="audit">The write-path audit service (SDD-AUDIT-001).</param>
    /// <param name="publishEndpoint">The transactional-outbox publish endpoint (SDD-INFRA-006).</param>
    /// <param name="currencyListCache">The reference-read cache for the active-currency list (SDD-INFRA-004).</param>
    /// <param name="currentUser">The authenticated-user accessor used to stamp audit rows.</param>
    public CurrencyService(
        NomenclatureDbContext db,
        IMapper mapper,
        ICorrelationIdAccessor correlation,
        ValidationChain<CreateCurrencyRequest> createChain,
        IAuditService audit,
        IPublishEndpoint publishEndpoint,
        ICacheService<IReadOnlyList<CurrencyDto>> currencyListCache,
        ICurrentUserAccessor currentUser)
        : base(db, mapper, correlation)
    {
        _createChain = createChain;
        _audit = audit;
        _publishEndpoint = publishEndpoint;
        _currencyListCache = currencyListCache;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public new Task<Result<PagedResult<CurrencyDto>>> SearchAsync(
        FilterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        FilterRequest effectiveRequest = ApplyDefaultSort(request);
        return base.SearchAsync(effectiveRequest, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<CurrencyDto>>> GetActiveAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CurrencyDto>? currencies = await _currencyListCache.GetOrSetAsync(
            CurrencyCacheKeys.ActiveCurrencies,
            LoadActiveCurrenciesAsync,
            ttl: null,
            cancellationToken).ConfigureAwait(false);

        return Result<IReadOnlyList<CurrencyDto>>.Success(currencies ?? []);
    }

    /// <inheritdoc />
    public async Task<Result<CurrencyDto>> GetByIsoCodeAsync(string isoCode, CancellationToken cancellationToken)
    {
        CurrencyDto? dto = await Db.Currencies
            .AsNoTracking()
            .Where(c => c.IsoCode == isoCode)
            .ProjectTo<CurrencyDto>(Mapper.ConfigurationProvider)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (dto is null)
        {
            return Result<CurrencyDto>.Failure(NomenclatureErrorCodes.CURRENCY_NOT_FOUND);
        }

        return Result<CurrencyDto>.Success(dto);
    }

    /// <inheritdoc />
    public async Task<Result<CurrencyDto>> CreateAsync(
        CreateCurrencyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ChainValidationResult validation =
            await _createChain.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return Result<CurrencyDto>.Failure(validation.ErrorCode!, validation.Detail);
        }

        Currency entity = new()
        {
            IsoCode = request.IsoCode,
            Name = request.Name,
            Symbol = request.Symbol,
            IsActive = request.IsActive,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Result<CurrencyDto> persisted = await PersistCreateAsync(entity, cancellationToken).ConfigureAwait(false);
        if (!persisted.IsSuccess)
        {
            return persisted;
        }

        await InvalidateRegionAsync(cancellationToken).ConfigureAwait(false);
        return persisted;
    }

    /// <inheritdoc />
    public async Task<Result<CurrencyDto>> UpdateAsync(
        string isoCode,
        UpdateCurrencyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Currency? entity = await Db.Currencies
            .FirstOrDefaultAsync(c => c.IsoCode == isoCode, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return Result<CurrencyDto>.Failure(NomenclatureErrorCodes.CURRENCY_NOT_FOUND);
        }

        Result tokenResult = ApplyConcurrencyToken(entity, request.RowVersion);
        if (!tokenResult.IsSuccess)
        {
            return Result<CurrencyDto>.Failure(tokenResult.ErrorCode!, tokenResult.Detail);
        }

        bool isDeactivation = entity.IsActive && !request.IsActive;
        string beforeJson = Serialize(Mapper.Map<CurrencyDto>(entity));

        entity.Name = request.Name;
        entity.Symbol = request.Symbol;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        Result<CurrencyDto> persisted =
            await PersistUpdateAsync(entity, beforeJson, isDeactivation, cancellationToken).ConfigureAwait(false);
        if (!persisted.IsSuccess)
        {
            return persisted;
        }

        await InvalidateRegionAsync(cancellationToken).ConfigureAwait(false);
        return persisted;
    }

    /// <summary>
    /// Applies the default ascending <c>IsoCode</c> ordering when the caller did not specify a sort
    /// (SDD-NOM-001 §2.1). The filtering library appends the primary key as the final deterministic term.
    /// </summary>
    /// <returns>A non-tracking ordered query over the currency set.</returns>
    protected override IQueryable<Currency> BuildBaseQuery()
    {
        return base.BuildBaseQuery().OrderBy(c => c.IsoCode);
    }

    private async Task<Result<CurrencyDto>> PersistCreateAsync(Currency entity, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        Db.Currencies.Add(entity);
        Result inserted = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!inserted.IsSuccess)
        {
            return Result<CurrencyDto>.Failure(inserted.ErrorCode!, inserted.Detail);
        }

        CurrencyDto dto = Mapper.Map<CurrencyDto>(entity);

        Result audited = await RecordAuditAsync(
            CurrencyAuditEventTypes.CurrencyCreated,
            AuditOperation.Create,
            entity,
            beforeJson: null,
            afterJson: Serialize(dto),
            reason: null,
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<CurrencyDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        await _publishEndpoint.Publish(BuildCreatedEvent(entity), cancellationToken).ConfigureAwait(false);

        Result flushed = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!flushed.IsSuccess)
        {
            return Result<CurrencyDto>.Failure(flushed.ErrorCode!, flushed.Detail);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result<CurrencyDto>.Success(dto);
    }

    private async Task<Result<CurrencyDto>> PersistUpdateAsync(
        Currency entity,
        string beforeJson,
        bool isDeactivation,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        CurrencyDto dto = Mapper.Map<CurrencyDto>(entity);

        Result audited = await RecordUpdateAuditAsync(
            entity, beforeJson, Serialize(dto), isDeactivation, cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<CurrencyDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        object domainEvent = isDeactivation
            ? BuildDeactivatedEvent(entity)
            : BuildUpdatedEvent(entity);
        await _publishEndpoint.Publish(domainEvent, cancellationToken).ConfigureAwait(false);

        Result saved = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            return Result<CurrencyDto>.Failure(saved.ErrorCode!, saved.Detail);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result<CurrencyDto>.Success(Mapper.Map<CurrencyDto>(entity));
    }

    private Task<Result> RecordUpdateAuditAsync(
        Currency entity,
        string beforeJson,
        string afterJson,
        bool isDeactivation,
        CancellationToken cancellationToken)
    {
        if (isDeactivation)
        {
            return RecordAuditAsync(
                CurrencyAuditEventTypes.CurrencyDeactivated,
                AuditOperation.StateChange,
                entity,
                beforeJson,
                afterJson,
                CurrencyAuditEventTypes.DefaultDeactivationReason,
                cancellationToken);
        }

        return RecordAuditAsync(
            CurrencyAuditEventTypes.CurrencyUpdated,
            AuditOperation.Update,
            entity,
            beforeJson,
            afterJson,
            reason: null,
            cancellationToken);
    }

    private Task<Result> RecordAuditAsync(
        string eventType,
        AuditOperation operation,
        Currency entity,
        string? beforeJson,
        string afterJson,
        string? reason,
        CancellationToken cancellationToken)
    {
        AuditEntry entry = new()
        {
            EventType = eventType,
            Operation = operation,
            EntityType = CurrencyAuditEventTypes.EntityType,
            EntityId = entity.Id.ToString(CultureInfo.InvariantCulture),
            UserId = _currentUser.GetUserId(),
            Username = _currentUser.GetUsername(),
            OccurredAt = DateTimeOffset.UtcNow,
            CorrelationId = Correlation.Get(),
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            Reason = reason
        };

        return _audit.RecordAsync(entry, cancellationToken);
    }

    private CurrencyCreatedEvent BuildCreatedEvent(Currency entity) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = Correlation.Get(),
        OccurredAt = DateTimeOffset.UtcNow,
        CurrencyId = entity.Id,
        IsoCode = entity.IsoCode,
        Name = entity.Name,
        Symbol = entity.Symbol,
        IsActive = entity.IsActive
    };

    private CurrencyUpdatedEvent BuildUpdatedEvent(Currency entity) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = Correlation.Get(),
        OccurredAt = DateTimeOffset.UtcNow,
        CurrencyId = entity.Id,
        IsoCode = entity.IsoCode,
        Name = entity.Name,
        Symbol = entity.Symbol,
        IsActive = entity.IsActive
    };

    private CurrencyDeactivatedEvent BuildDeactivatedEvent(Currency entity) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = Correlation.Get(),
        OccurredAt = DateTimeOffset.UtcNow,
        CurrencyId = entity.Id,
        IsoCode = entity.IsoCode,
        Name = entity.Name,
        Symbol = entity.Symbol,
        IsActive = entity.IsActive
    };

    private async Task<IReadOnlyList<CurrencyDto>?> LoadActiveCurrenciesAsync(CancellationToken cancellationToken)
    {
        List<CurrencyDto> currencies = await Db.Currencies
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.IsoCode)
            .ProjectTo<CurrencyDto>(Mapper.ConfigurationProvider)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return currencies;
    }

    private Task InvalidateRegionAsync(CancellationToken cancellationToken)
    {
        return _currencyListCache.RemoveByPatternAsync(CurrencyCacheKeys.InvalidationPattern, cancellationToken);
    }

    private static string Serialize(CurrencyDto dto) => JsonSerializer.Serialize(dto);

    private static FilterRequest ApplyDefaultSort(FilterRequest request)
    {
        if (request.Sort.Count > 0)
        {
            return request;
        }

        return request with
        {
            Sort = [new SortCriterion { Field = IsoCodeSortField, Direction = "asc" }]
        };
    }

    private Result ApplyConcurrencyToken(Currency entity, string rowVersion)
    {
        if (!TryDecodeRowVersion(rowVersion, out byte[] originalRowVersion))
        {
            return Result.Failure(
                CommonErrorCodes.CONCURRENT_MODIFICATION,
                "The supplied row version is not a valid base64 token.");
        }

        Db.Entry(entity).Property(c => c.RowVersion).OriginalValue = originalRowVersion;
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
}
