using System.Text.Json;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Finance.Accounts.API.Auditing;
using Finance.Accounts.API.Caching;
using Finance.Accounts.API.Interfaces;
using Finance.Accounts.DBModel;
using Finance.Accounts.DBModel.Models;
using Finance.Common.Abstractions;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Common.Validation;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Infrastructure.Audit.Models;
using Finance.Infrastructure.Caching.Interfaces;
using Finance.Infrastructure.Services;
using Finance.ServiceModel.Accounts;
using Finance.ServiceModel.Events.Accounts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Finance.Accounts.API.Services;

/// <summary>
/// Default <see cref="IAccountService"/> implementation built on the shared
/// <see cref="SearchableServiceBase{TEntity, TDto, TContext}"/> / <see cref="BaseEntityService{TContext}"/>
/// helpers. Every public method returns a <see cref="Result"/> / <see cref="Result{T}"/>
/// (SDD-ACCT-001 §2, SDD-INFRA-009). Writes record an audit row, publish a domain event via the
/// transactional outbox, and invalidate the reference-read cache region (SDD-AUDIT-001, SDD-INFRA-006,
/// SDD-INFRA-004).
/// </summary>
public sealed class AccountService
    : SearchableServiceBase<Account, AccountDto, AccountsDbContext>, IAccountService
{
    private const string CountryCodeSortField = nameof(Account.CountryCode);
    private const string CodeSortField = nameof(Account.Code);

    private readonly ValidationChain<CreateAccountRequest> _createChain;
    private readonly IAuditService _audit;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ICacheService<AccountDto> _accountCache;
    private readonly ICacheService<IReadOnlyList<AccountDto>> _chartCache;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Creates a new <see cref="AccountService"/>.</summary>
    /// <param name="db">The accounts database context.</param>
    /// <param name="mapper">The AutoMapper instance used for projection.</param>
    /// <param name="correlation">The ambient correlation-id accessor.</param>
    /// <param name="createChain">The cross-aggregate validation chain for account creation.</param>
    /// <param name="audit">The write-path audit service (SDD-AUDIT-001).</param>
    /// <param name="publishEndpoint">The transactional-outbox publish endpoint (SDD-INFRA-006).</param>
    /// <param name="accountCache">The reference-read cache for single accounts (SDD-INFRA-004).</param>
    /// <param name="chartCache">The reference-read cache for the active chart list (SDD-INFRA-004).</param>
    /// <param name="currentUser">The authenticated-user accessor used to stamp audit rows.</param>
    public AccountService(
        AccountsDbContext db,
        IMapper mapper,
        ICorrelationIdAccessor correlation,
        ValidationChain<CreateAccountRequest> createChain,
        IAuditService audit,
        IPublishEndpoint publishEndpoint,
        ICacheService<AccountDto> accountCache,
        ICacheService<IReadOnlyList<AccountDto>> chartCache,
        ICurrentUserAccessor currentUser)
        : base(db, mapper, correlation)
    {
        _createChain = createChain;
        _audit = audit;
        _publishEndpoint = publishEndpoint;
        _accountCache = accountCache;
        _chartCache = chartCache;
        _currentUser = currentUser;
    }

    /// <inheritdoc cref="IAccountService.SearchAsync" />
    public new Task<Result<PagedResult<AccountDto>>> SearchAsync(
        FilterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        FilterRequest effectiveRequest = ApplyDefaultSort(request);
        return base.SearchAsync(effectiveRequest, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<AccountDto>> GetAsync(int id, CancellationToken cancellationToken)
    {
        AccountDto? cached = await _accountCache.GetOrSetAsync(
            AccountCacheKeys.Account(id),
            ct => LoadAccountDtoAsync(id, ct),
            ttl: null,
            cancellationToken).ConfigureAwait(false);

        if (cached is null)
        {
            return Result<AccountDto>.Failure(AccountErrorCodes.ACCOUNT_NOT_FOUND);
        }

        return Result<AccountDto>.Success(cached);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<AccountDto>>> GetActiveChartAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<AccountDto>? chart = await _chartCache.GetOrSetAsync(
            AccountCacheKeys.ActiveChart,
            LoadActiveChartAsync,
            ttl: null,
            cancellationToken).ConfigureAwait(false);

        return Result<IReadOnlyList<AccountDto>>.Success(chart ?? []);
    }

    /// <inheritdoc />
    public async Task<Result<AccountDto>> CreateAsync(
        CreateAccountRequest request,
        string countryCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ChainValidationResult validation =
            await _createChain.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return Result<AccountDto>.Failure(validation.ErrorCode!, validation.Detail);
        }

        Account entity = new()
        {
            Code = request.Code,
            Name = request.Name,
            Type = request.Type,
            ParentId = request.ParentId,
            CountryCode = countryCode,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Result<AccountDto> persisted = await PersistCreateAsync(entity, cancellationToken).ConfigureAwait(false);
        if (!persisted.IsSuccess)
        {
            return persisted;
        }

        await InvalidateRegionAsync(cancellationToken).ConfigureAwait(false);
        return persisted;
    }

    /// <inheritdoc />
    public async Task<Result<AccountDto>> UpdateAsync(
        int id,
        UpdateAccountRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Account> found = await FindOrNotFoundAsync<Account>(
            id, AccountErrorCodes.ACCOUNT_NOT_FOUND, cancellationToken).ConfigureAwait(false);
        if (!found.IsSuccess)
        {
            return Result<AccountDto>.Failure(found.ErrorCode!, found.Detail);
        }

        Account entity = found.Value!;
        Result tokenResult = ApplyConcurrencyToken(entity, request.RowVersion);
        if (!tokenResult.IsSuccess)
        {
            return Result<AccountDto>.Failure(tokenResult.ErrorCode!, tokenResult.Detail);
        }

        bool isDeactivation = entity.IsActive && !request.IsActive;
        string beforeJson = Serialize(Mapper.Map<AccountDto>(entity));

        entity.Name = request.Name;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        Result<AccountDto> persisted =
            await PersistUpdateAsync(entity, beforeJson, isDeactivation, cancellationToken).ConfigureAwait(false);
        if (!persisted.IsSuccess)
        {
            return persisted;
        }

        await InvalidateRegionAsync(cancellationToken).ConfigureAwait(false);
        return persisted;
    }

    /// <summary>
    /// Applies the default <c>CountryCode</c> then <c>Code</c> ascending ordering (SDD-ACCT-001 §2.1).
    /// The filtering library appends the primary key as the final deterministic sort term.
    /// </summary>
    /// <returns>A non-tracking ordered query over the account set.</returns>
    protected override IQueryable<Account> BuildBaseQuery()
    {
        return base.BuildBaseQuery()
            .OrderBy(a => a.CountryCode)
            .ThenBy(a => a.Code);
    }

    private async Task<Result<AccountDto>> PersistCreateAsync(Account entity, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        Db.Accounts.Add(entity);
        Result inserted = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!inserted.IsSuccess)
        {
            return Result<AccountDto>.Failure(inserted.ErrorCode!, inserted.Detail);
        }

        AccountDto dto = Mapper.Map<AccountDto>(entity);

        Result audited = await RecordAuditAsync(
            AccountAuditEventTypes.AccountCreated,
            AuditOperation.Create,
            entity,
            beforeJson: null,
            afterJson: Serialize(dto),
            reason: null,
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<AccountDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        await _publishEndpoint.Publish(BuildCreatedEvent(entity), cancellationToken).ConfigureAwait(false);

        Result flushed = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!flushed.IsSuccess)
        {
            return Result<AccountDto>.Failure(flushed.ErrorCode!, flushed.Detail);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result<AccountDto>.Success(dto);
    }

    private async Task<Result<AccountDto>> PersistUpdateAsync(
        Account entity,
        string beforeJson,
        bool isDeactivation,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        AccountDto dto = Mapper.Map<AccountDto>(entity);

        Result audited = await RecordUpdateAuditAsync(
            entity, beforeJson, Serialize(dto), isDeactivation, cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<AccountDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        object domainEvent = isDeactivation
            ? BuildDeactivatedEvent(entity)
            : BuildUpdatedEvent(entity);
        await _publishEndpoint.Publish(domainEvent, cancellationToken).ConfigureAwait(false);

        Result saved = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            return Result<AccountDto>.Failure(saved.ErrorCode!, saved.Detail);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result<AccountDto>.Success(Mapper.Map<AccountDto>(entity));
    }

    private Task<Result> RecordUpdateAuditAsync(
        Account entity,
        string beforeJson,
        string afterJson,
        bool isDeactivation,
        CancellationToken cancellationToken)
    {
        if (isDeactivation)
        {
            return RecordAuditAsync(
                AccountAuditEventTypes.AccountDeactivated,
                AuditOperation.StateChange,
                entity,
                beforeJson,
                afterJson,
                AccountAuditEventTypes.DefaultDeactivationReason,
                cancellationToken);
        }

        return RecordAuditAsync(
            AccountAuditEventTypes.AccountUpdated,
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
        Account entity,
        string? beforeJson,
        string afterJson,
        string? reason,
        CancellationToken cancellationToken)
    {
        AuditEntry entry = new()
        {
            EventType = eventType,
            Operation = operation,
            EntityType = AccountAuditEventTypes.EntityType,
            EntityId = entity.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
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

    private AccountCreatedEvent BuildCreatedEvent(Account entity) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = Correlation.Get(),
        OccurredAt = DateTimeOffset.UtcNow,
        AccountId = entity.Id,
        Code = entity.Code,
        Name = entity.Name,
        Type = entity.Type,
        CountryCode = entity.CountryCode,
        IsActive = entity.IsActive
    };

    private AccountUpdatedEvent BuildUpdatedEvent(Account entity) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = Correlation.Get(),
        OccurredAt = DateTimeOffset.UtcNow,
        AccountId = entity.Id,
        Code = entity.Code,
        Name = entity.Name,
        Type = entity.Type,
        CountryCode = entity.CountryCode,
        IsActive = entity.IsActive
    };

    private AccountDeactivatedEvent BuildDeactivatedEvent(Account entity) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = Correlation.Get(),
        OccurredAt = DateTimeOffset.UtcNow,
        AccountId = entity.Id,
        Code = entity.Code,
        Name = entity.Name,
        Type = entity.Type,
        CountryCode = entity.CountryCode,
        IsActive = entity.IsActive
    };

    private async Task<AccountDto?> LoadAccountDtoAsync(int id, CancellationToken cancellationToken)
    {
        return await Db.Accounts
            .AsNoTracking()
            .Where(a => a.Id == id)
            .ProjectTo<AccountDto>(Mapper.ConfigurationProvider)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<AccountDto>?> LoadActiveChartAsync(CancellationToken cancellationToken)
    {
        List<AccountDto> chart = await Db.Accounts
            .AsNoTracking()
            .Where(a => a.IsActive)
            .OrderBy(a => a.CountryCode)
            .ThenBy(a => a.Code)
            .ProjectTo<AccountDto>(Mapper.ConfigurationProvider)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return chart;
    }

    private Task InvalidateRegionAsync(CancellationToken cancellationToken)
    {
        return _accountCache.RemoveByPatternAsync(AccountCacheKeys.InvalidationPattern, cancellationToken);
    }

    private static string Serialize(AccountDto dto) => JsonSerializer.Serialize(dto);

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
                new SortCriterion { Field = CountryCodeSortField, Direction = "asc" },
                new SortCriterion { Field = CodeSortField, Direction = "asc" }
            ]
        };
    }

    private Result ApplyConcurrencyToken(Account entity, string rowVersion)
    {
        if (!TryDecodeRowVersion(rowVersion, out byte[] originalRowVersion))
        {
            return Result.Failure(
                CommonErrorCodes.CONCURRENT_MODIFICATION,
                "The supplied row version is not a valid base64 token.");
        }

        Db.Entry(entity).Property(a => a.RowVersion).OriginalValue = originalRowVersion;
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
