using System.Text.Json;
using AutoMapper;
using Finance.Common.Abstractions;
using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Common.Validation;
using Finance.GenericFiltering;
using Finance.GenericFiltering.Exceptions;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Infrastructure.Audit.Models;
using Finance.Infrastructure.Services;
using Finance.Payments.API.Auditing;
using Finance.Payments.API.Interfaces;
using Finance.Payments.API.Validation;
using Finance.Payments.DBModel;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Events.Payments;
using Finance.ServiceModel.Payments;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Finance.Payments.API.Services;

/// <summary>
/// Default <see cref="IPaymentAllocationService"/> built on
/// <see cref="SearchableServiceBase{TEntity, TDto, TContext}"/> (SDD-PAY-002, SDD-INFRA-009). It owns the
/// sub-ledger matching layer: the allocation rows, the maintenance of the payment's allocated amount and the
/// local open item's settled amount, the derived settlement state, the audit-first write, and the transactional
/// outbox publish — all inside ONE transaction per call.
/// <para><b>Matching, not posting.</b> No path here creates, mutates, or reverses a journal entry, changes any
/// GL or trial-balance figure, changes the payment's status, or invokes
/// <c>IWorkflowEngine&lt;Payment&gt;</c> — which is why the engine is deliberately NOT a dependency. Allocation
/// rows are therefore mutable and removable: a mis-match is corrected by deleting the row, never by a
/// sign-flipped reversal.</para>
/// <para><b>Scoped list, not smuggled state.</b> The base <c>BuildBaseQuery()</c> takes no route argument, so the
/// payment-scoped list runs the SDD-INFRA-005 filter pipeline over an EXPLICITLY scoped query inside
/// <see cref="ListAsync"/>; the payment id is never held in mutable service state.</para>
/// <para><b>No caching.</b> Allocations, open items, settlement state, and outstanding balances are
/// transactional data — every read recomputes from the database.</para>
/// </summary>
public sealed class PaymentAllocationService
    : SearchableServiceBase<PaymentAllocation, PaymentAllocationDto, PaymentsDbContext>, IPaymentAllocationService
{
    private const string AllocatedAtSortField = nameof(PaymentAllocation.AllocatedAt);

    private readonly ValidationChain<PaymentAllocationValidationContext> _chain;
    private readonly AllocationAmountCalculator _amounts;
    private readonly SettlementStatusCalculator _settlement;
    private readonly IRealizedFxHandler _realizedFx;
    private readonly IAuditService _audit;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a new <see cref="PaymentAllocationService"/>.</summary>
    /// <param name="db">The payments database context.</param>
    /// <param name="mapper">The AutoMapper instance.</param>
    /// <param name="correlation">The ambient correlation-id accessor.</param>
    /// <param name="chain">The ten-rule cross-aggregate invariant chain (SDD-INFRA-007).</param>
    /// <param name="amounts">The per-row base-amount and realized-FX calculator.</param>
    /// <param name="settlement">The single derived-settlement-status calculator.</param>
    /// <param name="realizedFx">The dormant realized-FX seam invoked once per allocation row.</param>
    /// <param name="audit">The write-path audit service (SDD-AUDIT-001).</param>
    /// <param name="publishEndpoint">The transactional-outbox publish endpoint (SDD-INFRA-006).</param>
    /// <param name="currentUser">The authenticated-user accessor.</param>
    /// <param name="timeProvider">The clock stamping allocation times and the event ordering token.</param>
    public PaymentAllocationService(
        PaymentsDbContext db,
        IMapper mapper,
        ICorrelationIdAccessor correlation,
        ValidationChain<PaymentAllocationValidationContext> chain,
        AllocationAmountCalculator amounts,
        SettlementStatusCalculator settlement,
        IRealizedFxHandler realizedFx,
        IAuditService audit,
        IPublishEndpoint publishEndpoint,
        ICurrentUserAccessor currentUser,
        TimeProvider timeProvider)
        : base(db, mapper, correlation)
    {
        _chain = chain;
        _amounts = amounts;
        _settlement = settlement;
        _realizedFx = realizedFx;
        _audit = audit;
        _publishEndpoint = publishEndpoint;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<PaymentAllocationDto>>> ListAsync(
        Guid paymentId,
        FilterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        bool paymentExists = await Db.Payments
            .AnyAsync(payment => payment.Id == paymentId, cancellationToken)
            .ConfigureAwait(false);
        if (!paymentExists)
        {
            return Result<PagedResult<PaymentAllocationDto>>.Failure(PaymentErrorCodes.PAYMENT_NOT_FOUND);
        }

        try
        {
            PagedResult<PaymentAllocationDto> page =
                await BuildPageAsync(paymentId, ApplyDefaultSort(request), cancellationToken)
                    .ConfigureAwait(false);
            return Result<PagedResult<PaymentAllocationDto>>.Success(page);
        }
        catch (FilterValidationException ex)
        {
            return Result<PagedResult<PaymentAllocationDto>>.Failure(ex.ErrorCode, ex.Detail);
        }
    }

    /// <inheritdoc />
    public async Task<Result<AllocatePaymentResultDto>> AllocateAsync(
        Guid paymentId,
        AllocatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        Payment? payment = await LoadWithAllocationsAsync(paymentId, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            return Result<AllocatePaymentResultDto>.Failure(PaymentErrorCodes.PAYMENT_NOT_FOUND);
        }

        Result token = ApplyConcurrencyToken(payment, request.RowVersion);
        if (!token.IsSuccess)
        {
            return Result<AllocatePaymentResultDto>.Failure(token.ErrorCode!, token.Detail);
        }

        Dictionary<Guid, InvoiceOpenItem> openItems =
            await LoadOpenItemsAsync(request.Items, cancellationToken).ConfigureAwait(false);

        Result validated = await RunChainAsync(payment, request.Items, openItems, cancellationToken)
            .ConfigureAwait(false);
        if (!validated.IsSuccess)
        {
            return Result<AllocatePaymentResultDto>.Failure(validated.ErrorCode!, validated.Detail);
        }

        return await ApplyAllocationsAsync(payment, request.Items, openItems, transaction, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<DeallocatePaymentResultDto>> DeallocateAsync(
        Guid paymentId,
        int allocationId,
        string? rowVersion,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (allocationId <= 0)
        {
            return Result<DeallocatePaymentResultDto>.Failure(PaymentErrorCodes.PAYMENT_ALLOCATION_NOT_FOUND);
        }

        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        Payment? payment = await LoadWithAllocationsAsync(paymentId, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            return Result<DeallocatePaymentResultDto>.Failure(PaymentErrorCodes.PAYMENT_NOT_FOUND);
        }

        Result prepared = PrepareDeallocation(payment, rowVersion);
        if (!prepared.IsSuccess)
        {
            return Result<DeallocatePaymentResultDto>.Failure(prepared.ErrorCode!, prepared.Detail);
        }

        PaymentAllocation? allocation = payment.Allocations
            .FirstOrDefault(candidate => candidate.Id == allocationId);
        if (allocation is null)
        {
            return Result<DeallocatePaymentResultDto>.Failure(PaymentErrorCodes.PAYMENT_ALLOCATION_NOT_FOUND);
        }

        Result state = await RunChainAsync(payment, [], EmptyOpenItems(), cancellationToken)
            .ConfigureAwait(false);
        if (!state.IsSuccess)
        {
            return Result<DeallocatePaymentResultDto>.Failure(state.ErrorCode!, state.Detail);
        }

        return await ReleaseAllocationAsync(payment, allocation, reason, transaction, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<PagedResult<PaymentAllocationDto>> BuildPageAsync(
        Guid paymentId,
        FilterRequest request,
        CancellationToken cancellationToken)
    {
        IQueryable<PaymentAllocation> scoped = Db.PaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.PaymentId == paymentId);

        IQueryable<PaymentAllocation> filtered = scoped.ApplyFilterWithoutPaging(request);
        int totalCount = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);

        int page = request.Page < 1 ? 1 : request.Page;
        int skip = (page - 1) * request.PageSize;

        List<PaymentAllocation> rows = await filtered
            .Skip(skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, InvoiceOpenItem> openItems =
            await LoadOpenItemsForListAsync(rows, cancellationToken).ConfigureAwait(false);

        return new PagedResult<PaymentAllocationDto>
        {
            Items = rows.Select(row => MapRow(row, openItems)).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = request.PageSize
        };
    }

    private async Task<Dictionary<Guid, InvoiceOpenItem>> LoadOpenItemsForListAsync(
        IReadOnlyList<PaymentAllocation> rows,
        CancellationToken cancellationToken)
    {
        List<Guid> invoiceIds = rows.Select(row => row.InvoiceId).Distinct().ToList();

        List<InvoiceOpenItem> openItems = await Db.InvoiceOpenItems
            .AsNoTracking()
            .Where(item => invoiceIds.Contains(item.InvoiceId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return openItems.ToDictionary(item => item.InvoiceId);
    }

    private PaymentAllocationDto MapRow(
        PaymentAllocation allocation,
        IReadOnlyDictionary<Guid, InvoiceOpenItem> openItems)
    {
        openItems.TryGetValue(allocation.InvoiceId, out InvoiceOpenItem? openItem);

        PaymentAllocationProjectionRow row = new() { Allocation = allocation, OpenItem = openItem };
        PaymentAllocationDto dto = Mapper.Map<PaymentAllocationDto>(row);

        if (openItem is null)
        {
            return dto;
        }

        return dto with
        {
            InvoiceSettlementStatus = _settlement.Calculate(openItem.SettledAmount, openItem.GrossTotal)
        };
    }

    private async Task<Result<AllocatePaymentResultDto>> ApplyAllocationsAsync(
        Payment payment,
        IReadOnlyList<AllocatePaymentItem> items,
        IReadOnlyDictionary<Guid, InvoiceOpenItem> openItems,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        List<PaymentAllocation> existing = payment.Allocations.ToList();
        string beforeJson = SerializeMatching(payment, existing, removed: null);
        DateTimeOffset occurredAt = _timeProvider.GetUtcNow();

        List<PaymentAllocation> created = new(items.Count);
        foreach (AllocatePaymentItem item in items)
        {
            InvoiceOpenItem openItem = openItems[item.InvoiceId];
            PaymentAllocation allocation = BuildAllocation(payment, openItem, item, occurredAt);

            Result handled = await _realizedFx
                .HandleAsync(BuildFxContext(payment, openItem, allocation), cancellationToken)
                .ConfigureAwait(false);
            if (!handled.IsSuccess)
            {
                return Result<AllocatePaymentResultDto>.Failure(handled.ErrorCode!, handled.Detail);
            }

            Db.PaymentAllocations.Add(allocation);
            created.Add(allocation);

            payment.AllocatedAmount += item.AllocatedAmount;
            openItem.SettledAmount += item.AllocatedAmount;
        }

        string afterJson = SerializeMatching(payment, [.. existing, .. created], removed: null);

        Result recorded = await RecordAllocationAuditAsync(
            payment, created, beforeJson, afterJson, cancellationToken).ConfigureAwait(false);
        if (!recorded.IsSuccess)
        {
            return Result<AllocatePaymentResultDto>.Failure(recorded.ErrorCode!, recorded.Detail);
        }

        await PublishAllocatedEventsAsync(payment, created, openItems, occurredAt, cancellationToken)
            .ConfigureAwait(false);

        Result committed = await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
        if (!committed.IsSuccess)
        {
            return Result<AllocatePaymentResultDto>.Failure(committed.ErrorCode!, committed.Detail);
        }

        return Result<AllocatePaymentResultDto>.Success(BuildAllocateResult(payment, created, openItems));
    }

    private async Task<Result<DeallocatePaymentResultDto>> ReleaseAllocationAsync(
        Payment payment,
        PaymentAllocation allocation,
        string? reason,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        InvoiceOpenItem? openItem = await Db.InvoiceOpenItems
            .FirstOrDefaultAsync(item => item.InvoiceId == allocation.InvoiceId, cancellationToken)
            .ConfigureAwait(false);
        if (openItem is null)
        {
            return Result<DeallocatePaymentResultDto>.Failure(
                PaymentErrorCodes.PAYMENT_ALLOCATION_INVOICE_NOT_FOUND,
                $"No open item exists for invoice '{allocation.InvoiceId}'; the release cannot restate its settlement.");
        }

        List<PaymentAllocation> existing = payment.Allocations.ToList();
        List<PaymentAllocation> remaining = existing
            .Where(candidate => candidate.Id != allocation.Id)
            .ToList();

        string beforeJson = SerializeMatching(payment, existing, allocation);
        DateTimeOffset occurredAt = _timeProvider.GetUtcNow();

        payment.AllocatedAmount -= allocation.AllocatedAmount;
        openItem.SettledAmount -= allocation.AllocatedAmount;

        Result recorded = await RecordDeallocationAuditAsync(
            payment, remaining, beforeJson, reason, cancellationToken).ConfigureAwait(false);
        if (!recorded.IsSuccess)
        {
            return Result<DeallocatePaymentResultDto>.Failure(recorded.ErrorCode!, recorded.Detail);
        }

        await _publishEndpoint
            .Publish(BuildDeallocatedEvent(payment, allocation, openItem, occurredAt), cancellationToken)
            .ConfigureAwait(false);

        Db.PaymentAllocations.Remove(allocation);

        Result committed = await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
        if (!committed.IsSuccess)
        {
            return Result<DeallocatePaymentResultDto>.Failure(committed.ErrorCode!, committed.Detail);
        }

        return Result<DeallocatePaymentResultDto>.Success(BuildDeallocateResult(payment, allocation, openItem));
    }

    /// <summary>
    /// Applies the OPTIONAL payment row version supplied on the deallocate query string. When omitted, the
    /// tracked token loaded inside the transaction still guards a concurrent write (SDD-PAY-002 §2.6).
    /// </summary>
    /// <param name="payment">The tracked payment aggregate.</param>
    /// <param name="rowVersion">The optional base64 concurrency token.</param>
    /// <returns>A success result, or a concurrency failure for a malformed token.</returns>
    private Result PrepareDeallocation(Payment payment, string? rowVersion)
    {
        if (rowVersion is null)
        {
            return Result.Success();
        }

        return ApplyConcurrencyToken(payment, rowVersion);
    }

    private async Task<Result> RunChainAsync(
        Payment payment,
        IReadOnlyList<AllocatePaymentItem> items,
        IReadOnlyDictionary<Guid, InvoiceOpenItem> openItems,
        CancellationToken cancellationToken)
    {
        PaymentAllocationValidationContext context = new()
        {
            Payment = payment,
            Items = items,
            OpenItems = openItems
        };

        ChainValidationResult result = await _chain
            .ValidateAsync(context, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsValid)
        {
            return Result.Success();
        }

        return Result.Failure(result.ErrorCode!, result.Detail);
    }

    private PaymentAllocation BuildAllocation(
        Payment payment,
        InvoiceOpenItem openItem,
        AllocatePaymentItem item,
        DateTimeOffset occurredAt)
    {
        AllocationAmounts amounts = _amounts.Compute(payment, openItem, item.AllocatedAmount);

        return new PaymentAllocation
        {
            PaymentId = payment.Id,
            InvoiceId = item.InvoiceId,
            AllocatedAmount = item.AllocatedAmount,
            BaseAllocatedAmount = amounts.BaseAllocatedAmount,
            RealizedFxDifference = amounts.RealizedFxDifference,
            AllocatedAt = occurredAt,
            AllocatedBy = _currentUser.GetUserId(),
            CorrelationId = Correlation.Get()
        };
    }

    private RealizedFxContext BuildFxContext(
        Payment payment,
        InvoiceOpenItem openItem,
        PaymentAllocation allocation) => new()
    {
        PaymentId = payment.Id,
        InvoiceId = allocation.InvoiceId,
        Direction = payment.Direction,
        CurrencyCode = payment.CurrencyCode,
        BaseCurrencyCode = payment.BaseCurrencyCode,
        AllocatedAmount = allocation.AllocatedAmount,
        PaymentExchangeRate = payment.ExchangeRate,
        BookingExchangeRate = openItem.BookingExchangeRate,
        RealizedFxDifference = allocation.RealizedFxDifference,
        CorrelationId = allocation.CorrelationId
    };

    /// <summary>
    /// Writes one audit row per CREATED allocation, BEFORE any outbox row (audit-first, SDD-AUDIT-001). The
    /// audited subject is the PAYMENT whose matching changed, so the snapshots are the payment's matching
    /// projection — never the allocation row alone, which on this path is being created and therefore has no
    /// "before" state at all (SDD-PAY-002 §2.11).
    /// </summary>
    /// <param name="payment">The payment whose matching changed.</param>
    /// <param name="created">The allocation rows created by this call — one audit row is written per row.</param>
    /// <param name="beforeJson">The payment's pre-change matching snapshot.</param>
    /// <param name="afterJson">The same projection recomputed after the new rows.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result, or the first audit failure.</returns>
    private async Task<Result> RecordAllocationAuditAsync(
        Payment payment,
        IReadOnlyList<PaymentAllocation> created,
        string beforeJson,
        string afterJson,
        CancellationToken cancellationToken)
    {
        foreach (PaymentAllocation allocation in created)
        {
            Result recorded = await RecordAuditAsync(
                PaymentAuditEventTypes.PaymentAllocated,
                payment,
                beforeJson,
                afterJson,
                reason: null,
                cancellationToken).ConfigureAwait(false);
            if (!recorded.IsSuccess)
            {
                return recorded;
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Writes the single deallocate audit row BEFORE the outbox row (audit-first). The "before" snapshot is the
    /// payment's matching projection PLUS the removed row's own snapshot; the "after" snapshot is the projection
    /// without it (SDD-PAY-002 §2.11). An optional caller-supplied reason is persisted, though release is not a
    /// sensitive operation and needs none.
    /// </summary>
    /// <param name="payment">The payment whose matching changed.</param>
    /// <param name="remaining">The allocation rows left after the release.</param>
    /// <param name="beforeJson">The pre-release snapshot including the removed row.</param>
    /// <param name="reason">The optional operator-supplied reason.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result, or the audit failure.</returns>
    private Task<Result> RecordDeallocationAuditAsync(
        Payment payment,
        IReadOnlyList<PaymentAllocation> remaining,
        string beforeJson,
        string? reason,
        CancellationToken cancellationToken)
    {
        return RecordAuditAsync(
            PaymentAuditEventTypes.PaymentDeallocated,
            payment,
            beforeJson,
            SerializeMatching(payment, remaining, removed: null),
            reason,
            cancellationToken);
    }

    private Task<Result> RecordAuditAsync(
        string eventType,
        Payment payment,
        string beforeJson,
        string afterJson,
        string? reason,
        CancellationToken cancellationToken)
    {
        AuditEntry audit = new()
        {
            EventType = eventType,
            Operation = AuditOperation.Update,
            EntityType = PaymentAuditEventTypes.EntityType,
            EntityId = payment.Id.ToString(),
            UserId = _currentUser.GetUserId(),
            Username = _currentUser.GetUsername(),
            OccurredAt = _timeProvider.GetUtcNow(),
            CorrelationId = Correlation.Get(),
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            Reason = reason
        };

        return _audit.RecordAsync(audit, cancellationToken);
    }

    /// <summary>
    /// Enqueues exactly ONE allocation event per created row to the transactional outbox — never one aggregate
    /// event per multi-item request — so the Invoices-side settlement consumer stays per-invoice and idempotent.
    /// <para>The event's ordering timestamp is the <paramref name="occurredAt"/> stamped INSIDE this transaction
    /// and is never re-stamped at dispatch or publish time (SDD-PAY-002 §2.10).</para>
    /// </summary>
    /// <param name="payment">The payment whose matching changed.</param>
    /// <param name="created">The allocation rows created by this call.</param>
    /// <param name="openItems">The touched open items carrying their post-change settled amounts.</param>
    /// <param name="occurredAt">The in-transaction server timestamp used as the ordering token.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task that completes when every event has been enqueued.</returns>
    private async Task PublishAllocatedEventsAsync(
        Payment payment,
        IReadOnlyList<PaymentAllocation> created,
        IReadOnlyDictionary<Guid, InvoiceOpenItem> openItems,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        foreach (PaymentAllocation allocation in created)
        {
            InvoiceOpenItem openItem = openItems[allocation.InvoiceId];
            await _publishEndpoint
                .Publish(BuildAllocatedEvent(payment, allocation, openItem, occurredAt), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private PaymentAllocatedEvent BuildAllocatedEvent(
        Payment payment,
        PaymentAllocation allocation,
        InvoiceOpenItem openItem,
        DateTimeOffset occurredAt) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = allocation.CorrelationId,
        OccurredAt = occurredAt,
        PaymentId = payment.Id,
        DocumentNumber = payment.DocumentNumber!,
        InvoiceId = allocation.InvoiceId,
        Direction = payment.Direction,
        CounterpartyId = payment.CounterpartyId,
        CurrencyCode = payment.CurrencyCode,
        AllocatedAmount = allocation.AllocatedAmount,
        BaseAllocatedAmount = allocation.BaseAllocatedAmount,
        RealizedFxDifference = allocation.RealizedFxDifference,
        InvoiceSettledAmount = openItem.SettledAmount,
        InvoiceSettlementStatus = DeriveStatus(openItem),
        AllocatedAt = allocation.AllocatedAt
    };

    private PaymentDeallocatedEvent BuildDeallocatedEvent(
        Payment payment,
        PaymentAllocation allocation,
        InvoiceOpenItem openItem,
        DateTimeOffset occurredAt) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = Correlation.Get(),
        OccurredAt = occurredAt,
        PaymentId = payment.Id,
        DocumentNumber = payment.DocumentNumber!,
        InvoiceId = allocation.InvoiceId,
        ReleasedAmount = allocation.AllocatedAmount,
        BaseReleasedAmount = allocation.BaseAllocatedAmount,
        InvoiceSettledAmount = openItem.SettledAmount,
        InvoiceSettlementStatus = DeriveStatus(openItem),
        DeallocatedAt = occurredAt
    };

    private AllocatePaymentResultDto BuildAllocateResult(
        Payment payment,
        IReadOnlyList<PaymentAllocation> created,
        IReadOnlyDictionary<Guid, InvoiceOpenItem> openItems) => new()
    {
        PaymentId = payment.Id,
        Allocations = created.Select(allocation => MapRow(allocation, openItems)).ToList(),
        AllocatedAmount = payment.AllocatedAmount,
        UnallocatedAmount = payment.UnallocatedAmount,
        RowVersion = Convert.ToBase64String(payment.RowVersion),
        AffectedInvoices = created
            .Select(allocation => openItems[allocation.InvoiceId])
            .DistinctBy(openItem => openItem.InvoiceId)
            .Select(BuildSettlementDto)
            .ToList()
    };

    private DeallocatePaymentResultDto BuildDeallocateResult(
        Payment payment,
        PaymentAllocation allocation,
        InvoiceOpenItem openItem) => new()
    {
        PaymentId = payment.Id,
        AllocationId = allocation.Id,
        InvoiceId = allocation.InvoiceId,
        ReleasedAmount = allocation.AllocatedAmount,
        AllocatedAmount = payment.AllocatedAmount,
        UnallocatedAmount = payment.UnallocatedAmount,
        RowVersion = Convert.ToBase64String(payment.RowVersion),
        AffectedInvoice = BuildSettlementDto(openItem)
    };

    private AllocatedInvoiceSettlementDto BuildSettlementDto(InvoiceOpenItem openItem) => new()
    {
        InvoiceId = openItem.InvoiceId,
        SettledAmount = openItem.SettledAmount,
        SettlementStatus = DeriveStatus(openItem)
    };

    private SettlementStatus DeriveStatus(InvoiceOpenItem openItem) =>
        _settlement.Calculate(openItem.SettledAmount, openItem.GrossTotal);

    private async Task<Result> CommitAsync(
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        Result saved = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            return saved;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private Task<Payment?> LoadWithAllocationsAsync(Guid id, CancellationToken cancellationToken)
    {
        return Db.Payments
            .Include(payment => payment.Allocations)
            .FirstOrDefaultAsync(payment => payment.Id == id, cancellationToken);
    }

    private async Task<Dictionary<Guid, InvoiceOpenItem>> LoadOpenItemsAsync(
        IReadOnlyList<AllocatePaymentItem> items,
        CancellationToken cancellationToken)
    {
        List<Guid> invoiceIds = items.Select(item => item.InvoiceId).Distinct().ToList();

        List<InvoiceOpenItem> openItems = await Db.InvoiceOpenItems
            .Where(item => invoiceIds.Contains(item.InvoiceId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return openItems.ToDictionary(item => item.InvoiceId);
    }

    private Result ApplyConcurrencyToken(Payment payment, string rowVersion)
    {
        if (!TryDecodeRowVersion(rowVersion, out byte[] originalRowVersion))
        {
            return Result.Failure(
                CommonErrorCodes.CONCURRENT_MODIFICATION,
                "The supplied row version is not a valid base64 token.");
        }

        Db.Entry(payment).Property(entity => entity.RowVersion).OriginalValue = originalRowVersion;
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

    /// <summary>
    /// Serializes the PAYMENT's matching projection — its allocated amount, its computed unallocated amount, and
    /// its <c>(InvoiceId, AllocatedAmount)</c> pairs — for the audit snapshots (SDD-PAY-002 §2.11). An empty pair
    /// list is still a non-empty JSON object, so the audit layer's non-empty <c>BeforeJson</c> invariant holds
    /// even for a payment's first-ever allocation.
    /// <para>The rows are passed in EXPLICITLY rather than read from the tracked navigation, so the snapshot
    /// never depends on EF relationship fixup timing.</para>
    /// </summary>
    /// <param name="payment">The payment whose matching is being snapshotted.</param>
    /// <param name="rows">The allocation rows the snapshot projects.</param>
    /// <param name="removed">The row being released, included verbatim in the deallocate "before" snapshot.</param>
    /// <returns>The JSON snapshot.</returns>
    private static string SerializeMatching(
        Payment payment,
        IReadOnlyList<PaymentAllocation> rows,
        PaymentAllocation? removed)
    {
        decimal allocated = rows.Sum(allocation => allocation.AllocatedAmount);

        return JsonSerializer.Serialize(new
        {
            PaymentId = payment.Id,
            AllocatedAmount = allocated,
            UnallocatedAmount = payment.Amount - allocated,
            Allocations = rows
                .Select(allocation => new { allocation.InvoiceId, allocation.AllocatedAmount })
                .ToList(),
            RemovedAllocation = removed is null
                ? null
                : (object)new { removed.Id, removed.InvoiceId, removed.AllocatedAmount }
        });
    }

    private static IReadOnlyDictionary<Guid, InvoiceOpenItem> EmptyOpenItems() =>
        new Dictionary<Guid, InvoiceOpenItem>();

    /// <summary>
    /// Applies the documented default ordering (allocation time descending) when the caller supplies none. The
    /// filtering library always appends the primary key as the final sort term, so paging stays deterministic
    /// (SDD-PAY-002 §2.7).
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
            Sort = [new SortCriterion { Field = AllocatedAtSortField, Direction = "desc" }]
        };
    }
}
