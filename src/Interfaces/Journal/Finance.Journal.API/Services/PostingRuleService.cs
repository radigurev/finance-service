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
using Finance.Journal.API.Auditing;
using Finance.Journal.API.Caching;
using Finance.Journal.API.Interfaces;
using Finance.Journal.API.Validators;
using Finance.Journal.DBModel;
using Finance.Journal.DBModel.Models;
using Finance.ServiceModel.Posting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Finance.Journal.API.Services;

/// <summary>
/// Default <see cref="IPostingRuleService"/> built on <see cref="SearchableServiceBase{TEntity, TDto, TContext}"/>
/// (SDD-FIN-006 §2.1, SDD-INFRA-009). Posting rules are reference data: single reads are cached, writes
/// are audited (CoA-style) and invalidate the cache (SDD-INFRA-004, SDD-AUDIT-001). No domain event is
/// published — posting rules are configuration, not a business transaction (SDD-FIN-006 §1).
/// </summary>
public sealed class PostingRuleService
    : SearchableServiceBase<PostingRule, PostingRuleDto, JournalDbContext>, IPostingRuleService
{
    private const string RuleKeySortField = nameof(PostingRule.RuleKey);

    private readonly ValidationChain<CreatePostingRuleRequest> _createChain;
    private readonly IAuditService _audit;
    private readonly ICacheService<PostingRuleDto> _ruleCache;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Creates a new <see cref="PostingRuleService"/>.</summary>
    /// <param name="db">The journal database context.</param>
    /// <param name="mapper">The AutoMapper instance used for projection.</param>
    /// <param name="correlation">The ambient correlation-id accessor.</param>
    /// <param name="createChain">The cross-aggregate validation chain for rule creation.</param>
    /// <param name="audit">The write-path audit service (SDD-AUDIT-001).</param>
    /// <param name="ruleCache">The reference-read cache for single rules (SDD-INFRA-004).</param>
    /// <param name="currentUser">The authenticated-user accessor used to stamp audit rows.</param>
    public PostingRuleService(
        JournalDbContext db,
        IMapper mapper,
        ICorrelationIdAccessor correlation,
        ValidationChain<CreatePostingRuleRequest> createChain,
        IAuditService audit,
        ICacheService<PostingRuleDto> ruleCache,
        ICurrentUserAccessor currentUser)
        : base(db, mapper, correlation)
    {
        _createChain = createChain;
        _audit = audit;
        _ruleCache = ruleCache;
        _currentUser = currentUser;
    }

    /// <inheritdoc cref="IPostingRuleService.SearchAsync" />
    public new Task<Result<PagedResult<PostingRuleDto>>> SearchAsync(
        FilterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return base.SearchAsync(ApplyDefaultSort(request), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<PostingRuleDto>> GetAsync(int id, CancellationToken cancellationToken)
    {
        PostingRuleDto? cached = await _ruleCache.GetOrSetAsync(
            PostingRuleCacheKeys.ById(id),
            ct => LoadRuleDtoAsync(id, ct),
            ttl: null,
            cancellationToken).ConfigureAwait(false);

        if (cached is null)
        {
            return Result<PostingRuleDto>.Failure(PostingErrorCodes.POSTING_RULE_NOT_FOUND);
        }

        return Result<PostingRuleDto>.Success(cached);
    }

    /// <inheritdoc />
    public async Task<Result<PostingRuleDto>> CreateAsync(
        CreatePostingRuleRequest request,
        string countryCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ChainValidationResult validation =
            await _createChain.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return Result<PostingRuleDto>.Failure(validation.ErrorCode!, validation.Detail);
        }

        PostingRule entity = BuildRule(request, countryCode);

        Result<PostingRuleDto> persisted =
            await PersistCreateAsync(entity, cancellationToken).ConfigureAwait(false);
        if (!persisted.IsSuccess)
        {
            return persisted;
        }

        await InvalidateRegionAsync(cancellationToken).ConfigureAwait(false);
        return persisted;
    }

    /// <inheritdoc />
    public async Task<Result<PostingRuleDto>> UpdateAsync(
        int id,
        UpdatePostingRuleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ChainValidationResult balance = PostingRuleStructure.ValidateBalanceable(request.Lines);
        if (!balance.IsValid)
        {
            return Result<PostingRuleDto>.Failure(balance.ErrorCode!, balance.Detail);
        }

        Result<PostingRule> found = await FindTrackedRuleAsync(id, cancellationToken).ConfigureAwait(false);
        if (!found.IsSuccess)
        {
            return Result<PostingRuleDto>.Failure(found.ErrorCode!, found.Detail);
        }

        return await ApplyUpdateAsync(found.Value!, request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Orders the posting-rule list by <c>RuleKey</c> ascending (SDD-FIN-006 §2.1).</summary>
    /// <returns>The non-tracking ordered base query.</returns>
    protected override IQueryable<PostingRule> BuildBaseQuery()
    {
        return base.BuildBaseQuery().OrderBy(rule => rule.RuleKey);
    }

    private async Task<Result<PostingRuleDto>> ApplyUpdateAsync(
        PostingRule entity,
        UpdatePostingRuleRequest request,
        CancellationToken cancellationToken)
    {
        Result tokenResult = ApplyConcurrencyToken(entity, request.RowVersion);
        if (!tokenResult.IsSuccess)
        {
            return Result<PostingRuleDto>.Failure(tokenResult.ErrorCode!, tokenResult.Detail);
        }

        bool isDeactivation = entity.IsActive && !request.IsActive;
        string beforeJson = Serialize(Mapper.Map<PostingRuleDto>(entity));

        entity.Description = request.Description;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        ReplaceLines(entity, request.Lines);

        Result<PostingRuleDto> persisted =
            await PersistUpdateAsync(entity, beforeJson, isDeactivation, cancellationToken).ConfigureAwait(false);
        if (!persisted.IsSuccess)
        {
            return persisted;
        }

        await InvalidateRegionAsync(cancellationToken).ConfigureAwait(false);
        return persisted;
    }

    private async Task<Result<PostingRuleDto>> PersistCreateAsync(
        PostingRule entity,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        Db.PostingRules.Add(entity);
        Result inserted = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!inserted.IsSuccess)
        {
            return Result<PostingRuleDto>.Failure(inserted.ErrorCode!, inserted.Detail);
        }

        PostingRuleDto dto = Mapper.Map<PostingRuleDto>(entity);
        Result audited = await RecordAuditAsync(
            PostingRuleAuditEventTypes.PostingRuleCreated,
            AuditOperation.Create,
            entity,
            beforeJson: null,
            Serialize(dto),
            reason: null,
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<PostingRuleDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        Result flushed = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!flushed.IsSuccess)
        {
            return Result<PostingRuleDto>.Failure(flushed.ErrorCode!, flushed.Detail);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result<PostingRuleDto>.Success(dto);
    }

    private async Task<Result<PostingRuleDto>> PersistUpdateAsync(
        PostingRule entity,
        string beforeJson,
        bool isDeactivation,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        PostingRuleDto dto = Mapper.Map<PostingRuleDto>(entity);
        Result audited = await RecordUpdateAuditAsync(
            entity, beforeJson, Serialize(dto), isDeactivation, cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<PostingRuleDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        Result saved = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            return Result<PostingRuleDto>.Failure(saved.ErrorCode!, saved.Detail);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result<PostingRuleDto>.Success(Mapper.Map<PostingRuleDto>(entity));
    }

    private async Task<Result<PostingRule>> FindTrackedRuleAsync(int id, CancellationToken cancellationToken)
    {
        PostingRule? entity = await Db.PostingRules
            .Include(rule => rule.Lines)
            .FirstOrDefaultAsync(rule => rule.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return Result<PostingRule>.Failure(PostingErrorCodes.POSTING_RULE_NOT_FOUND);
        }

        return Result<PostingRule>.Success(entity);
    }

    private void ReplaceLines(PostingRule entity, IReadOnlyList<CreatePostingRuleLineRequest> lines)
    {
        Db.PostingRuleLines.RemoveRange(entity.Lines);
        entity.Lines.Clear();

        int lineNumber = 1;
        foreach (CreatePostingRuleLineRequest line in lines)
        {
            entity.Lines.Add(new PostingRuleLine
            {
                LineNumber = lineNumber++,
                AccountSelector = line.AccountSelector,
                DebitOrCredit = line.DebitOrCredit,
                AmountSource = line.AmountSource
            });
        }
    }

    private static PostingRule BuildRule(CreatePostingRuleRequest request, string countryCode)
    {
        PostingRule entity = new()
        {
            RuleKey = request.RuleKey,
            Description = request.Description,
            CountryCode = countryCode,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        int lineNumber = 1;
        foreach (CreatePostingRuleLineRequest line in request.Lines)
        {
            entity.Lines.Add(new PostingRuleLine
            {
                LineNumber = lineNumber++,
                AccountSelector = line.AccountSelector,
                DebitOrCredit = line.DebitOrCredit,
                AmountSource = line.AmountSource
            });
        }

        return entity;
    }

    private Task<Result> RecordUpdateAuditAsync(
        PostingRule entity,
        string beforeJson,
        string afterJson,
        bool isDeactivation,
        CancellationToken cancellationToken)
    {
        if (isDeactivation)
        {
            return RecordAuditAsync(
                PostingRuleAuditEventTypes.PostingRuleDeactivated,
                AuditOperation.StateChange,
                entity,
                beforeJson,
                afterJson,
                PostingRuleAuditEventTypes.DefaultDeactivationReason,
                cancellationToken);
        }

        return RecordAuditAsync(
            PostingRuleAuditEventTypes.PostingRuleUpdated,
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
        PostingRule entity,
        string? beforeJson,
        string afterJson,
        string? reason,
        CancellationToken cancellationToken)
    {
        AuditEntry entry = new()
        {
            EventType = eventType,
            Operation = operation,
            EntityType = PostingRuleAuditEventTypes.EntityType,
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

    private async Task<PostingRuleDto?> LoadRuleDtoAsync(int id, CancellationToken cancellationToken)
    {
        return await Db.PostingRules
            .AsNoTracking()
            .Where(rule => rule.Id == id)
            .ProjectTo<PostingRuleDto>(Mapper.ConfigurationProvider)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private Task InvalidateRegionAsync(CancellationToken cancellationToken)
    {
        return _ruleCache.RemoveByPatternAsync(PostingRuleCacheKeys.InvalidationPattern, cancellationToken);
    }

    private Result ApplyConcurrencyToken(PostingRule entity, string rowVersion)
    {
        if (!TryDecodeRowVersion(rowVersion, out byte[] originalRowVersion))
        {
            return Result.Failure(
                CommonErrorCodes.CONCURRENT_MODIFICATION,
                "The supplied row version is not a valid base64 token.");
        }

        Db.Entry(entity).Property(rule => rule.RowVersion).OriginalValue = originalRowVersion;
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

    private static string Serialize(PostingRuleDto dto) => JsonSerializer.Serialize(dto);

    private static FilterRequest ApplyDefaultSort(FilterRequest request)
    {
        if (request.Sort.Count > 0)
        {
            return request;
        }

        return request with
        {
            Sort = [new SortCriterion { Field = RuleKeySortField, Direction = "asc" }]
        };
    }
}
