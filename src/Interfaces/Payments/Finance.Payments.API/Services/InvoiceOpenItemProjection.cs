using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Common.Settlement;
using Finance.Payments.API.Interfaces;
using Finance.Payments.DBModel;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Events.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Finance.Payments.API.Services;

/// <summary>
/// Default <see cref="IInvoiceOpenItemProjection"/> (SDD-PAY-002 §2.3). Maintains the local
/// <see cref="InvoiceOpenItem"/> read projection from the invoice service's own immutable domain events, so
/// allocation and aging never cross-join <c>finance_invoices</c> and never depend on that service being
/// reachable.
/// <para>Every apply is a convergent UPSERT keyed by the invoice identifier; the terminal statuses are never
/// left; and the locally-owned settled amount is never written here. A <c>DbUpdateConcurrencyException</c> from a
/// concurrent allocation is deliberately NOT swallowed — it propagates so MassTransit retries and the second
/// attempt sees the applied change.</para>
/// </summary>
public sealed class InvoiceOpenItemProjection : IInvoiceOpenItemProjection
{
    private const decimal FallbackBookingExchangeRate = 1.000000m;

    private static readonly IReadOnlySet<string> TerminalStatuses = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(InvoiceStatus.Cancelled),
        nameof(InvoiceStatus.Reversed)
    };

    private readonly PaymentsDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InvoiceOpenItemProjection> _logger;

    /// <summary>Creates a new <see cref="InvoiceOpenItemProjection"/>.</summary>
    /// <param name="db">The payments database context that owns the projection table.</param>
    /// <param name="timeProvider">The clock stamping the last-applied timestamp.</param>
    /// <param name="logger">The structured logger for admission skips and orphaned-settlement warnings.</param>
    public InvoiceOpenItemProjection(
        PaymentsDbContext db,
        TimeProvider timeProvider,
        ILogger<InvoiceOpenItemProjection> logger)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _db = db;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> ApplyConfirmedAsync(
        InvoiceConfirmedEvent message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!SettlementPairing.IsSettleableInvoiceType(message.DocumentType))
        {
            _logger.LogInformation(
                "Skipping open-item projection for invoice {InvoiceId}: document type {DocumentType} is not "
                + "settleable by any payment document type, so it is excluded from matching and aging.",
                message.InvoiceId,
                message.DocumentType);
            return Result.Success();
        }

        InvoiceOpenItem? existing = await LoadAsync(message.InvoiceId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            _db.InvoiceOpenItems.Add(BuildFromConfirmation(message));
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }

        if (IsTerminal(existing))
        {
            LogTerminalNoOp(existing, nameof(InvoiceConfirmedEvent));
            return Result.Success();
        }

        RefreshExternallyOwnedColumns(existing, message);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result> ApplyPostedAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        return ApplyStatusToExistingAsync(
            invoiceId,
            nameof(InvoiceStatus.Posted),
            nameof(InvoicePostedEvent),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result> ApplyReversedAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        return ApplyStatusToExistingAsync(
            invoiceId,
            nameof(InvoiceStatus.Reversed),
            nameof(InvoiceReversedEvent),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result> ApplyCancelledAsync(
        InvoiceCancelledEvent message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        InvoiceOpenItem? existing = await LoadAsync(message.InvoiceId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            _db.InvoiceOpenItems.Add(BuildTombstone(message));
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Tombstoned open item for cancelled invoice {InvoiceId} the projection had not seen; the row is "
                + "excluded from every allocation and aging path.",
                message.InvoiceId);
            return Result.Success();
        }

        await WarnOnOrphanedSettlementAsync(message.InvoiceId, cancellationToken).ConfigureAwait(false);

        if (IsTerminal(existing))
        {
            LogTerminalNoOp(existing, nameof(InvoiceCancelledEvent));
            return Result.Success();
        }

        existing.InvoiceStatus = nameof(InvoiceStatus.Cancelled);
        existing.LastAppliedAt = _timeProvider.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result> ApplyStatusToExistingAsync(
        Guid invoiceId,
        string targetStatus,
        string eventName,
        CancellationToken cancellationToken)
    {
        InvoiceOpenItem? existing = await LoadAsync(invoiceId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            _logger.LogWarning(
                "No open item exists for invoice {InvoiceId} while applying {EventName}; failing for retry "
                + "rather than inventing a partial row.",
                invoiceId,
                eventName);

            return Result.Failure(
                PaymentErrorCodes.PAYMENT_ALLOCATION_INVOICE_NOT_FOUND,
                $"No open item exists for invoice '{invoiceId}' while applying {eventName}.");
        }

        if (IsTerminal(existing))
        {
            LogTerminalNoOp(existing, eventName);
            return Result.Success();
        }

        existing.InvoiceStatus = targetStatus;
        existing.LastAppliedAt = _timeProvider.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private Task<InvoiceOpenItem?> LoadAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        return _db.InvoiceOpenItems
            .FirstOrDefaultAsync(item => item.InvoiceId == invoiceId, cancellationToken);
    }

    private async Task WarnOnOrphanedSettlementAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        bool hasAllocations = await _db.PaymentAllocations
            .AnyAsync(allocation => allocation.InvoiceId == invoiceId, cancellationToken)
            .ConfigureAwait(false);

        if (!hasAllocations)
        {
            return;
        }

        _logger.LogWarning(
            "Orphaned settlement detected: invoice {InvoiceId} was cancelled while payment allocations still "
            + "point at it. The allocations are NOT auto-released; repair is operator deallocation.",
            invoiceId);
    }

    private InvoiceOpenItem BuildFromConfirmation(InvoiceConfirmedEvent message) => new()
    {
        InvoiceId = message.InvoiceId,
        DocumentNumber = message.DocumentNumber,
        DocumentType = message.DocumentType.ToString(),
        Direction = message.Direction.ToString(),
        CounterpartyId = message.CounterpartyId,
        CurrencyCode = message.CurrencyCode,
        BaseCurrencyCode = message.BaseCurrencyCode,
        GrossTotal = message.GrossTotal,
        BookingExchangeRate = message.BookingExchangeRate ?? FallbackBookingExchangeRate,
        IssueDate = message.IssueDate,
        DueDate = message.DueDate ?? message.IssueDate,
        InvoiceStatus = nameof(InvoiceStatus.Confirmed),
        SettledAmount = 0m,
        LastAppliedAt = _timeProvider.GetUtcNow()
    };

    /// <summary>
    /// Refreshes only the EXTERNALLY-owned columns of an existing non-terminal row, leaving the locally-owned
    /// settled amount untouched. A late confirmation never downgrades an already-<c>Posted</c> row: the status
    /// moves to <c>Confirmed</c> only when the row is still <c>Confirmed</c>.
    /// </summary>
    /// <param name="existing">The tracked projection row to refresh.</param>
    /// <param name="message">The confirmation event supplying the externally-owned values.</param>
    private void RefreshExternallyOwnedColumns(InvoiceOpenItem existing, InvoiceConfirmedEvent message)
    {
        existing.DocumentNumber = message.DocumentNumber;
        existing.DocumentType = message.DocumentType.ToString();
        existing.Direction = message.Direction.ToString();
        existing.CounterpartyId = message.CounterpartyId;
        existing.CurrencyCode = message.CurrencyCode;
        existing.BaseCurrencyCode = message.BaseCurrencyCode;
        existing.GrossTotal = message.GrossTotal;
        existing.BookingExchangeRate = message.BookingExchangeRate ?? FallbackBookingExchangeRate;
        existing.IssueDate = message.IssueDate;
        existing.DueDate = message.DueDate ?? message.IssueDate;
        existing.LastAppliedAt = _timeProvider.GetUtcNow();
    }

    /// <summary>
    /// Builds a cancellation TOMBSTONE for an invoice the projection never saw: the status is
    /// <c>Cancelled</c>, the document number is taken from the event when present (it is null for a draft
    /// cancel), and every unknown externally-owned column stays at its zero/default value. Those placeholders
    /// are never read, because a cancelled row is excluded from every allocation and aging path.
    /// </summary>
    /// <param name="message">The cancellation event.</param>
    /// <returns>The tombstone row to insert.</returns>
    private static InvoiceOpenItem BuildTombstone(InvoiceCancelledEvent message) => new()
    {
        InvoiceId = message.InvoiceId,
        DocumentNumber = message.DocumentNumber ?? string.Empty,
        DocumentType = string.Empty,
        Direction = string.Empty,
        CounterpartyId = Guid.Empty,
        CurrencyCode = string.Empty,
        BaseCurrencyCode = string.Empty,
        GrossTotal = 0m,
        BookingExchangeRate = FallbackBookingExchangeRate,
        IssueDate = message.OccurredAt,
        DueDate = message.OccurredAt,
        InvoiceStatus = nameof(InvoiceStatus.Cancelled),
        SettledAmount = 0m,
        LastAppliedAt = message.OccurredAt
    };

    private static bool IsTerminal(InvoiceOpenItem item) => TerminalStatuses.Contains(item.InvoiceStatus);

    private void LogTerminalNoOp(InvoiceOpenItem item, string eventName)
    {
        _logger.LogInformation(
            "Open item for invoice {InvoiceId} is terminal ({InvoiceStatus}); {EventName} is a no-op and the "
            + "row is left untouched.",
            item.InvoiceId,
            item.InvoiceStatus,
            eventName);
    }
}
