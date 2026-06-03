using System.Text.Json;
using AutoMapper;
using Finance.Common.Abstractions;
using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Common.Workflow;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Infrastructure.Audit.Models;
using Finance.Infrastructure.Sequences;
using Finance.Infrastructure.Sequences.Interfaces;
using Finance.Infrastructure.Services;
using Finance.Journal.API.Auditing;
using Finance.Journal.API.Interfaces;
using Finance.Journal.API.Validation;
using Finance.Journal.DBModel;
using Finance.Journal.DBModel.Models;
using Finance.ServiceModel.Events.Journal;
using Finance.ServiceModel.Journal;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Finance.Journal.API.Services;

/// <summary>
/// Default <see cref="IJournalEntryService"/> built on <see cref="SearchableServiceBase{TEntity, TDto, TContext}"/>
/// (SDD-FIN-001, SDD-FIN-002, SDD-INFRA-009). Posting and reversal run through
/// <see cref="IWorkflowEngine{TAggregate}"/>, allocate a gapless <c>JE</c> number, write an audit row, and
/// publish a domain event via the transactional outbox — all inside one transaction. Posted entries are
/// immutable; corrections are made by sign-flipped reversal. Journal entries are never cached.
/// </summary>
public sealed class JournalEntryService
    : SearchableServiceBase<JournalEntry, JournalEntryDto, JournalDbContext>, IJournalEntryService
{
    private const string EntryDateSortField = nameof(JournalEntry.EntryDate);

    private readonly IJournalEntryValidator _validator;
    private readonly IWorkflowEngine<JournalEntry> _workflow;
    private readonly ISequenceGenerator _sequence;
    private readonly IAuditService _audit;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Creates a new <see cref="JournalEntryService"/>.</summary>
    /// <param name="db">The journal database context.</param>
    /// <param name="mapper">The AutoMapper instance.</param>
    /// <param name="correlation">The ambient correlation-id accessor.</param>
    /// <param name="validator">The double-entry validation surface (SDD-FIN-001).</param>
    /// <param name="workflow">The journal-entry workflow engine (SDD-INFRA-008).</param>
    /// <param name="sequence">The gapless sequence generator (SDD-INFRA-003).</param>
    /// <param name="audit">The write-path audit service (SDD-AUDIT-001).</param>
    /// <param name="publishEndpoint">The transactional-outbox publish endpoint (SDD-INFRA-006).</param>
    /// <param name="currentUser">The authenticated-user accessor.</param>
    public JournalEntryService(
        JournalDbContext db,
        IMapper mapper,
        ICorrelationIdAccessor correlation,
        IJournalEntryValidator validator,
        IWorkflowEngine<JournalEntry> workflow,
        ISequenceGenerator sequence,
        IAuditService audit,
        IPublishEndpoint publishEndpoint,
        ICurrentUserAccessor currentUser)
        : base(db, mapper, correlation)
    {
        _validator = validator;
        _workflow = workflow;
        _sequence = sequence;
        _audit = audit;
        _publishEndpoint = publishEndpoint;
        _currentUser = currentUser;
    }

    /// <inheritdoc cref="IJournalEntryService.SearchAsync" />
    public new Task<Result<PagedResult<JournalEntryDto>>> SearchAsync(
        FilterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return base.SearchAsync(ApplyDefaultSort(request), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<JournalEntryDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        JournalEntry? entry = await LoadWithLinesAsync(id, tracking: false, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return Result<JournalEntryDto>.Failure(JournalErrorCodes.JOURNAL_ENTRY_NOT_FOUND);
        }

        return Result<JournalEntryDto>.Success(Mapper.Map<JournalEntryDto>(entry));
    }

    /// <inheritdoc />
    public async Task<Result<JournalEntryDto>> CreateDraftAsync(
        CreateJournalEntryRequest request,
        string baseCurrencyCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result validation = await ValidateLinesAsync(
            baseCurrencyCode, request.Lines, cancellationToken).ConfigureAwait(false);
        if (!validation.IsSuccess)
        {
            return Result<JournalEntryDto>.Failure(validation.ErrorCode!, validation.Detail);
        }

        JournalEntry entry = BuildDraft(request, baseCurrencyCode);
        return await PersistDraftAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<JournalEntryDto>> UpdateDraftAsync(
        Guid id,
        UpdateJournalEntryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        JournalEntry? entry = await LoadWithLinesAsync(id, tracking: true, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return Result<JournalEntryDto>.Failure(JournalErrorCodes.JOURNAL_ENTRY_NOT_FOUND);
        }

        if (entry.Status != JournalEntryStatus.Draft)
        {
            return Result<JournalEntryDto>.Failure(JournalErrorCodes.CANNOT_EDIT_POSTED_ENTRY);
        }

        Result tokenResult = ApplyConcurrencyToken(entry, request.RowVersion);
        if (!tokenResult.IsSuccess)
        {
            return Result<JournalEntryDto>.Failure(tokenResult.ErrorCode!, tokenResult.Detail);
        }

        Result validation = await ValidateLinesAsync(
            entry.BaseCurrencyCode, request.Lines, cancellationToken).ConfigureAwait(false);
        if (!validation.IsSuccess)
        {
            return Result<JournalEntryDto>.Failure(validation.ErrorCode!, validation.Detail);
        }

        return await PersistDraftUpdateAsync(entry, request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken)
    {
        JournalEntry? entry = await LoadWithLinesAsync(id, tracking: true, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return Result.Failure(JournalErrorCodes.JOURNAL_ENTRY_NOT_FOUND);
        }

        if (entry.Status != JournalEntryStatus.Draft)
        {
            return Result.Failure(JournalErrorCodes.CANNOT_EDIT_POSTED_ENTRY);
        }

        return await PersistDraftDeleteAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<JournalEntryDto>> PostAsync(
        Guid id,
        PostJournalEntryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        JournalEntry? entry = await LoadWithLinesAsync(id, tracking: true, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return Result<JournalEntryDto>.Failure(JournalErrorCodes.JOURNAL_ENTRY_NOT_FOUND);
        }

        if (entry.Status != JournalEntryStatus.Draft)
        {
            return Result<JournalEntryDto>.Failure(JournalErrorCodes.ENTRY_NOT_DRAFT);
        }

        Result tokenResult = ApplyConcurrencyToken(entry, request.RowVersion);
        if (!tokenResult.IsSuccess)
        {
            return Result<JournalEntryDto>.Failure(tokenResult.ErrorCode!, tokenResult.Detail);
        }

        Result validation = await ValidateEntryAsync(entry, cancellationToken).ConfigureAwait(false);
        if (!validation.IsSuccess)
        {
            return Result<JournalEntryDto>.Failure(validation.ErrorCode!, validation.Detail);
        }

        return await PostInTransactionAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<JournalEntryDto>> ReverseAsync(
        Guid id,
        ReverseJournalEntryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result<JournalEntryDto>.Failure(JournalErrorCodes.REVERSAL_REASON_REQUIRED);
        }

        JournalEntry? original = await LoadWithLinesAsync(id, tracking: true, cancellationToken).ConfigureAwait(false);
        if (original is null)
        {
            return Result<JournalEntryDto>.Failure(JournalErrorCodes.JOURNAL_ENTRY_NOT_FOUND);
        }

        if (original.Status != JournalEntryStatus.Posted)
        {
            return Result<JournalEntryDto>.Failure(JournalErrorCodes.INVALID_JOURNAL_STATE_TRANSITION);
        }

        Result tokenResult = ApplyConcurrencyToken(original, request.RowVersion);
        if (!tokenResult.IsSuccess)
        {
            return Result<JournalEntryDto>.Failure(tokenResult.ErrorCode!, tokenResult.Detail);
        }

        return await ReverseInTransactionAsync(original, request.Reason, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Default-orders the list by descending <c>EntryDate</c> (SDD-FIN-002 §2.9).</summary>
    /// <returns>A non-tracking ordered query over the entry set.</returns>
    protected override IQueryable<JournalEntry> BuildBaseQuery()
    {
        return base.BuildBaseQuery().OrderByDescending(entry => entry.EntryDate);
    }

    private async Task<Result> ValidateEntryAsync(JournalEntry entry, CancellationToken cancellationToken)
    {
        IReadOnlyList<JournalEntryLineRequest> lines =
        [
            .. entry.Lines.Select(line => new JournalEntryLineRequest
            {
                AccountId = line.AccountId,
                DebitAmount = line.DebitAmount,
                CreditAmount = line.CreditAmount,
                CurrencyCode = line.CurrencyCode,
                ExchangeRate = line.ExchangeRate,
                BaseDebitAmount = line.BaseDebitAmount,
                BaseCreditAmount = line.BaseCreditAmount,
                Description = line.Description
            })
        ];

        return await ValidateLinesAsync(entry.BaseCurrencyCode, lines, cancellationToken).ConfigureAwait(false);
    }

    private Task<Result> ValidateLinesAsync(
        string baseCurrencyCode,
        IReadOnlyList<JournalEntryLineRequest> lines,
        CancellationToken cancellationToken)
    {
        JournalEntryValidationContext context = new()
        {
            BaseCurrencyCode = baseCurrencyCode,
            Lines = lines
        };

        return _validator.ValidateAsync(context, cancellationToken);
    }

    private JournalEntry BuildDraft(CreateJournalEntryRequest request, string baseCurrencyCode)
    {
        Guid userId = _currentUser.GetUserId();
        string correlationId = Correlation.Get();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        JournalEntry entry = new()
        {
            EntryDate = request.EntryDate,
            Description = request.Description,
            BaseCurrencyCode = baseCurrencyCode,
            Status = JournalEntryStatus.Draft,
            CorrelationId = correlationId,
            CreatedAt = now,
            CreatedBy = userId,
            Lines = MapLines(request.Lines)
        };

        return entry;
    }

    private static List<JournalEntryLine> MapLines(IReadOnlyList<JournalEntryLineRequest> requests)
    {
        List<JournalEntryLine> lines = new(requests.Count);
        int lineNumber = 1;
        foreach (JournalEntryLineRequest request in requests)
        {
            lines.Add(new JournalEntryLine
            {
                AccountId = request.AccountId,
                DebitAmount = request.DebitAmount,
                CreditAmount = request.CreditAmount,
                CurrencyCode = request.CurrencyCode,
                ExchangeRate = request.ExchangeRate,
                BaseDebitAmount = request.BaseDebitAmount,
                BaseCreditAmount = request.BaseCreditAmount,
                LineNumber = lineNumber++,
                Description = request.Description
            });
        }

        return lines;
    }

    private async Task<Result<JournalEntryDto>> PersistDraftAsync(
        JournalEntry entry,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        Db.JournalEntries.Add(entry);
        Result inserted = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!inserted.IsSuccess)
        {
            return Result<JournalEntryDto>.Failure(inserted.ErrorCode!, inserted.Detail);
        }

        Result audited = await RecordAuditAsync(
            JournalAuditEventTypes.JournalEntryCreated,
            AuditOperation.Create,
            entry,
            beforeJson: null,
            afterJson: SerializeEntry(entry),
            reason: null,
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<JournalEntryDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        Result flushed = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!flushed.IsSuccess)
        {
            return Result<JournalEntryDto>.Failure(flushed.ErrorCode!, flushed.Detail);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result<JournalEntryDto>.Success(Mapper.Map<JournalEntryDto>(entry));
    }

    private async Task<Result<JournalEntryDto>> PersistDraftUpdateAsync(
        JournalEntry entry,
        UpdateJournalEntryRequest request,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        string beforeJson = SerializeEntry(entry);

        entry.EntryDate = request.EntryDate;
        entry.Description = request.Description;
        Db.JournalEntryLines.RemoveRange(entry.Lines);
        entry.Lines = MapLines(request.Lines);

        Result audited = await RecordAuditAsync(
            JournalAuditEventTypes.JournalEntryUpdated,
            AuditOperation.Update,
            entry,
            beforeJson,
            SerializeEntry(entry),
            reason: null,
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<JournalEntryDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        Result saved = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            return Result<JournalEntryDto>.Failure(saved.ErrorCode!, saved.Detail);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result<JournalEntryDto>.Success(Mapper.Map<JournalEntryDto>(entry));
    }

    private async Task<Result> PersistDraftDeleteAsync(JournalEntry entry, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        string beforeJson = SerializeEntry(entry);

        Result audited = await RecordAuditAsync(
            JournalAuditEventTypes.JournalEntryDeleted,
            AuditOperation.Delete,
            entry,
            beforeJson,
            afterJson: "{\"deleted\":true}",
            reason: null,
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result.Failure(audited.ErrorCode!, audited.Detail);
        }

        Db.JournalEntries.Remove(entry);

        Result saved = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            return saved;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result<JournalEntryDto>> PostInTransactionAsync(
        JournalEntry entry,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        string beforeJson = SerializeEntry(entry);

        Result transition = await TransitionAsync(
            entry, JournalEntryStatus.Posted, reason: null, cancellationToken).ConfigureAwait(false);
        if (!transition.IsSuccess)
        {
            return Result<JournalEntryDto>.Failure(transition.ErrorCode!, transition.Detail);
        }

        string entryNumber = await _sequence.NextAsync(SequenceKeys.JournalEntry, cancellationToken).ConfigureAwait(false);
        StampPosted(entry, entryNumber);
        AppendStatusHistory(entry, JournalEntryStatus.Draft, JournalEntryStatus.Posted, reason: null);

        Result audited = await RecordAuditAsync(
            JournalAuditEventTypes.JournalEntryPosted,
            AuditOperation.StateChange,
            entry,
            beforeJson,
            SerializeEntry(entry),
            reason: null,
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<JournalEntryDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        await _publishEndpoint.Publish(BuildPostedEvent(entry), cancellationToken).ConfigureAwait(false);

        Result saved = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            return Result<JournalEntryDto>.Failure(saved.ErrorCode!, saved.Detail);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result<JournalEntryDto>.Success(Mapper.Map<JournalEntryDto>(entry));
    }

    private async Task<Result<JournalEntryDto>> ReverseInTransactionAsync(
        JournalEntry original,
        string reason,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        string originalBefore = SerializeEntry(original);

        Result transition = await TransitionAsync(
            original, JournalEntryStatus.Reversed, reason, cancellationToken).ConfigureAwait(false);
        if (!transition.IsSuccess)
        {
            return Result<JournalEntryDto>.Failure(transition.ErrorCode!, transition.Detail);
        }

        JournalEntry reversal = BuildReversal(original);
        Db.JournalEntries.Add(reversal);

        string reversalNumber = await _sequence.NextAsync(SequenceKeys.JournalEntry, cancellationToken).ConfigureAwait(false);
        StampPosted(reversal, reversalNumber);
        AppendStatusHistory(original, JournalEntryStatus.Posted, JournalEntryStatus.Reversed, reason);
        AppendStatusHistory(reversal, fromStatus: null, JournalEntryStatus.Posted, reason: null);

        Result audited = await RecordReversalAuditAsync(
            original, originalBefore, reversal, reason, cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<JournalEntryDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        await _publishEndpoint.Publish(
            BuildReversedEvent(original, reversal, reason), cancellationToken).ConfigureAwait(false);

        Result saved = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            return Result<JournalEntryDto>.Failure(saved.ErrorCode!, saved.Detail);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result<JournalEntryDto>.Success(Mapper.Map<JournalEntryDto>(reversal));
    }

    private async Task<Result> TransitionAsync(
        JournalEntry entry,
        JournalEntryStatus target,
        string? reason,
        CancellationToken cancellationToken)
    {
        WorkflowContext<JournalEntry> context = new()
        {
            Aggregate = entry,
            CurrentState = entry.Status.ToString(),
            TargetState = target.ToString(),
            Reason = reason,
            CorrelationId = Correlation.Get()
        };

        Result transition = await _workflow.TransitionAsync(context, cancellationToken).ConfigureAwait(false);
        if (!transition.IsSuccess)
        {
            return Result.Failure(TranslateTransitionCode(transition), transition.Detail);
        }

        entry.Status = target;
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
            return JournalErrorCodes.INVALID_JOURNAL_STATE_TRANSITION;
        }

        return transition.ErrorCode!;
    }

    private void StampPosted(JournalEntry entry, string entryNumber)
    {
        entry.EntryNumber = entryNumber;
        entry.PostedAt = DateTimeOffset.UtcNow;
        entry.PostedBy = _currentUser.GetUserId();
        entry.Status = JournalEntryStatus.Posted;
    }

    private void AppendStatusHistory(
        JournalEntry entry,
        JournalEntryStatus? fromStatus,
        JournalEntryStatus toStatus,
        string? reason)
    {
        entry.StatusHistory.Add(new JournalEntryStatusHistory
        {
            FromStatus = fromStatus?.ToString(),
            ToStatus = toStatus.ToString(),
            ChangedBy = _currentUser.GetUserId(),
            ChangedAt = DateTimeOffset.UtcNow,
            CorrelationId = Correlation.Get(),
            Reason = reason
        });
    }

    private JournalEntry BuildReversal(JournalEntry original)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        JournalEntry reversal = new()
        {
            Id = Guid.NewGuid(),
            EntryDate = original.EntryDate,
            Description = $"Reversal of {original.EntryNumber}",
            BaseCurrencyCode = original.BaseCurrencyCode,
            Status = JournalEntryStatus.Posted,
            ReversesEntryId = original.Id,
            CorrelationId = Correlation.Get(),
            CreatedAt = now,
            CreatedBy = _currentUser.GetUserId(),
            Lines = FlipLines(original.Lines)
        };

        return reversal;
    }

    private static List<JournalEntryLine> FlipLines(IEnumerable<JournalEntryLine> lines)
    {
        List<JournalEntryLine> flipped = [];
        int lineNumber = 1;
        foreach (JournalEntryLine line in lines.OrderBy(line => line.LineNumber))
        {
            flipped.Add(new JournalEntryLine
            {
                AccountId = line.AccountId,
                DebitAmount = line.CreditAmount,
                CreditAmount = line.DebitAmount,
                CurrencyCode = line.CurrencyCode,
                ExchangeRate = line.ExchangeRate,
                BaseDebitAmount = line.BaseCreditAmount,
                BaseCreditAmount = line.BaseDebitAmount,
                LineNumber = lineNumber++,
                Description = line.Description
            });
        }

        return flipped;
    }

    private Task<Result> RecordReversalAuditAsync(
        JournalEntry original,
        string originalBefore,
        JournalEntry reversal,
        string reason,
        CancellationToken cancellationToken)
    {
        return RecordAuditAsync(
            JournalAuditEventTypes.JournalEntryReversed,
            AuditOperation.StateChange,
            original,
            originalBefore,
            SerializeEntry(original),
            reason,
            cancellationToken);
    }

    private Task<Result> RecordAuditAsync(
        string eventType,
        AuditOperation operation,
        JournalEntry entry,
        string? beforeJson,
        string afterJson,
        string? reason,
        CancellationToken cancellationToken)
    {
        AuditEntry audit = new()
        {
            EventType = eventType,
            Operation = operation,
            EntityType = JournalAuditEventTypes.EntityType,
            EntityId = entry.Id.ToString(),
            UserId = _currentUser.GetUserId(),
            Username = _currentUser.GetUsername(),
            OccurredAt = DateTimeOffset.UtcNow,
            CorrelationId = Correlation.Get(),
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            Reason = reason
        };

        return _audit.RecordAsync(audit, cancellationToken);
    }

    private JournalEntryPostedEvent BuildPostedEvent(JournalEntry entry) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = Correlation.Get(),
        OccurredAt = DateTimeOffset.UtcNow,
        JournalEntryId = entry.Id,
        EntryNumber = entry.EntryNumber!,
        EntryDate = entry.EntryDate,
        BaseCurrencyCode = entry.BaseCurrencyCode,
        Lines =
        [
            .. entry.Lines.OrderBy(line => line.LineNumber).Select(line => new ServiceModel.Events.Journal.JournalEntryPostedLine
            {
                AccountId = line.AccountId,
                DebitAmount = line.DebitAmount,
                CreditAmount = line.CreditAmount,
                CurrencyCode = line.CurrencyCode,
                ExchangeRate = line.ExchangeRate,
                BaseDebitAmount = line.BaseDebitAmount,
                BaseCreditAmount = line.BaseCreditAmount
            })
        ]
    };

    private JournalEntryReversedEvent BuildReversedEvent(
        JournalEntry original,
        JournalEntry reversal,
        string reason) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = Correlation.Get(),
        OccurredAt = DateTimeOffset.UtcNow,
        OriginalJournalEntryId = original.Id,
        ReversalJournalEntryId = reversal.Id,
        ReversalEntryNumber = reversal.EntryNumber!,
        Reason = reason
    };

    private Task<JournalEntry?> LoadWithLinesAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        IQueryable<JournalEntry> query = Db.JournalEntries
            .Include(entry => entry.Lines)
            .Where(entry => entry.Id == id);

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return query.FirstOrDefaultAsync(cancellationToken);
    }

    private Result ApplyConcurrencyToken(JournalEntry entry, string rowVersion)
    {
        if (!TryDecodeRowVersion(rowVersion, out byte[] originalRowVersion))
        {
            return Result.Failure(
                CommonErrorCodes.CONCURRENT_MODIFICATION,
                "The supplied row version is not a valid base64 token.");
        }

        Db.Entry(entry).Property(e => e.RowVersion).OriginalValue = originalRowVersion;
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

    private static string SerializeEntry(JournalEntry entry)
    {
        return JsonSerializer.Serialize(new
        {
            entry.Id,
            entry.EntryNumber,
            entry.EntryDate,
            entry.Description,
            entry.BaseCurrencyCode,
            Status = entry.Status.ToString(),
            entry.ReversesEntryId,
            entry.PostedAt,
            Lines = entry.Lines.OrderBy(line => line.LineNumber).Select(line => new
            {
                line.AccountId,
                line.DebitAmount,
                line.CreditAmount,
                line.CurrencyCode,
                line.ExchangeRate,
                line.BaseDebitAmount,
                line.BaseCreditAmount,
                line.LineNumber
            })
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
            Sort = [new SortCriterion { Field = EntryDateSortField, Direction = "desc" }]
        };
    }
}
