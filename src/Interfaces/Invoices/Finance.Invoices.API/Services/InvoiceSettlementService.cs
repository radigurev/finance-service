using System.Text.Json;
using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Infrastructure.Audit.Models;
using Finance.Invoices.API.Auditing;
using Finance.Invoices.API.Interfaces;
using Finance.Invoices.DBModel;
using Finance.Invoices.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Finance.Invoices.API.Services;

/// <summary>
/// Default <see cref="IInvoiceSettlementService"/> (SDD-INV-001 §2.14/§2.15). Maintains
/// <see cref="Invoice.SettledAmount"/>, the derived <see cref="Invoice.SettlementStatus"/>, and the
/// <see cref="Invoice.LastSettlementAppliedAt"/> ordering token from SDD-PAY-002's allocation events.
/// <para>The mirror is ORDERED, not merely replay-safe. Absolute assignment alone makes a replay of the SAME
/// message harmless but is NOT commutative across DIFFERENT messages, and the <c>RowVersion</c> serialization
/// below is itself a reordering mechanism: the loser of a race retries and would otherwise assign its OLDER
/// absolute value last, freezing the invoice at the lower figure forever. The guarantee is therefore
/// last-writer-by-<c>OccurredAt</c> — strictly older events are DROPPED — and NOT self-convergence: a dropped
/// event is never replayed, so the mirror does not repair itself.</para>
/// <para>A <see cref="DbUpdateConcurrencyException"/> from two events landing on the same invoice concurrently
/// is deliberately NOT swallowed: it propagates so MassTransit retries and the second attempt re-reads the
/// applied ordering token.</para>
/// </summary>
public sealed class InvoiceSettlementService : IInvoiceSettlementService
{
    private readonly InvoicesDbContext _db;
    private readonly InvoiceSettlementStatusCalculator _calculator;
    private readonly IAuditService _audit;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly ILogger<InvoiceSettlementService> _logger;

    /// <summary>Creates a new <see cref="InvoiceSettlementService"/>.</summary>
    /// <param name="db">The invoices database context owning the mirrored columns.</param>
    /// <param name="calculator">The single settlement-status derivation (SDD-INV-001 §2.14).</param>
    /// <param name="audit">The write-path audit service (SDD-AUDIT-001).</param>
    /// <param name="currentUser">The identity accessor, resolving to the system identity for consumers.</param>
    /// <param name="logger">The structured logger for stale drops and derivation disagreements.</param>
    public InvoiceSettlementService(
        InvoicesDbContext db,
        InvoiceSettlementStatusCalculator calculator,
        IAuditService audit,
        ICurrentUserAccessor currentUser,
        ILogger<InvoiceSettlementService> logger)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(calculator);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(logger);

        _db = db;
        _calculator = calculator;
        _audit = audit;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> ApplyAsync(InvoiceSettlementUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        Invoice? invoice = await _db.Invoices
            .IgnoreAutoIncludes()
            .FirstOrDefaultAsync(candidate => candidate.Id == update.InvoiceId, cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
        {
            _logger.LogWarning(
                "No invoice exists for {InvoiceId} while applying {SourceEvent}; failing for retry rather than "
                + "creating a placeholder invoice. CorrelationId={CorrelationId}",
                update.InvoiceId,
                update.SourceEvent,
                update.CorrelationId);

            return Result.Failure(
                InvoiceErrorCodes.INVOICE_NOT_FOUND,
                $"No invoice exists for '{update.InvoiceId}' while applying {update.SourceEvent}.");
        }

        if (IsStale(invoice, update))
        {
            LogStaleDrop(invoice, update);
            return Result.Success();
        }

        Result ceiling = AssertCeiling(invoice, update);
        if (!ceiling.IsSuccess)
        {
            return ceiling;
        }

        return await ApplyInTransactionAsync(invoice, update, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Evaluates the ordering token: an event whose <c>OccurredAt</c> is STRICTLY older than the invoice's
    /// last-applied token is stale, while an equal token (a genuine second event sharing a timestamp) and a
    /// <c>null</c> token (no event applied yet) are applied.
    /// </summary>
    /// <param name="invoice">The tracked invoice carrying the ordering token.</param>
    /// <param name="update">The settlement update under evaluation.</param>
    /// <returns><c>true</c> when the event MUST be dropped as a silent, successful no-op.</returns>
    private static bool IsStale(Invoice invoice, InvoiceSettlementUpdate update)
    {
        return invoice.LastSettlementAppliedAt is DateTimeOffset lastApplied
            && update.OccurredAt < lastApplied;
    }

    /// <summary>
    /// Asserts the defensive settlement invariant: the authoritative amount MUST be within
    /// <c>[0.00, GrossTotal]</c> by exact decimal comparison. SDD-PAY-002 §2.5 forbids over-allocation at the
    /// source, so a breach is unreachable through the sanctioned path and is rejected — never clamped,
    /// truncated, or silently persisted.
    /// </summary>
    /// <param name="invoice">The tracked invoice supplying the gross-total ceiling.</param>
    /// <param name="update">The settlement update under evaluation.</param>
    /// <returns>A success result, or the ceiling-breach failure the consumer turns into a throw.</returns>
    private Result AssertCeiling(Invoice invoice, InvoiceSettlementUpdate update)
    {
        if (update.SettledAmount >= 0m && update.SettledAmount <= invoice.GrossTotal)
        {
            return Result.Success();
        }

        _logger.LogError(
            "Settlement update from {SourceEvent} for invoice {InvoiceId} carries {SettledAmount} which breaches "
            + "the [0.00, {GrossTotal}] ceiling; rejecting for retry rather than clamping. "
            + "CorrelationId={CorrelationId}",
            update.SourceEvent,
            invoice.Id,
            update.SettledAmount,
            invoice.GrossTotal,
            update.CorrelationId);

        return Result.Failure(
            InvoiceErrorCodes.INVOICE_SETTLEMENT_EXCEEDS_GROSS_TOTAL,
            $"Settled amount {update.SettledAmount} is outside [0.00, {invoice.GrossTotal}] for invoice "
            + $"'{invoice.Id}'.");
    }

    private async Task<Result> ApplyInTransactionAsync(
        Invoice invoice,
        InvoiceSettlementUpdate update,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        string beforeJson = SerializeSettlement(invoice);
        SettlementStatus derived = _calculator.Calculate(update.SettledAmount, invoice.GrossTotal);
        WarnOnDerivationDisagreement(invoice, update, derived);

        invoice.SettledAmount = update.SettledAmount;
        invoice.SettlementStatus = derived;
        invoice.LastSettlementAppliedAt = update.OccurredAt;

        Result audited = await RecordAuditAsync(
            invoice,
            update,
            beforeJson,
            SerializeSettlement(invoice),
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return audited;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Applied {SourceEvent} to invoice {InvoiceId}: SettledAmount={SettledAmount}, "
            + "SettlementStatus={SettlementStatus}, LastSettlementAppliedAt={LastSettlementAppliedAt}. "
            + "CorrelationId={CorrelationId}",
            update.SourceEvent,
            invoice.Id,
            invoice.SettledAmount,
            invoice.SettlementStatus,
            invoice.LastSettlementAppliedAt,
            update.CorrelationId);

        return Result.Success();
    }

    /// <summary>
    /// Logs a structured warning when the publishing service's derived status disagrees with the local
    /// recomputation. The local value is authoritative for this database and MUST NOT be replaced by the remote
    /// one (SDD-INV-001 §2.15 step 4).
    /// </summary>
    /// <param name="invoice">The invoice being updated.</param>
    /// <param name="update">The settlement update carrying the reported status.</param>
    /// <param name="derived">The locally derived status.</param>
    private void WarnOnDerivationDisagreement(
        Invoice invoice,
        InvoiceSettlementUpdate update,
        SettlementStatus derived)
    {
        if (derived == update.ReportedStatus)
        {
            return;
        }

        _logger.LogWarning(
            "Settlement derivation disagreement on invoice {InvoiceId} from {SourceEvent}: reported "
            + "{ReportedStatus}, locally derived {DerivedStatus} from SettledAmount={SettledAmount} and "
            + "GrossTotal={GrossTotal}. Keeping the local value. CorrelationId={CorrelationId}",
            invoice.Id,
            update.SourceEvent,
            update.ReportedStatus,
            derived,
            update.SettledAmount,
            invoice.GrossTotal,
            update.CorrelationId);
    }

    private void LogStaleDrop(Invoice invoice, InvoiceSettlementUpdate update)
    {
        _logger.LogInformation(
            "Dropping stale {SourceEvent} for invoice {InvoiceId}: OccurredAt={OccurredAt} is older than the "
            + "applied token {LastSettlementAppliedAt}, so the newer authoritative total stands. "
            + "CorrelationId={CorrelationId}",
            update.SourceEvent,
            invoice.Id,
            update.OccurredAt,
            invoice.LastSettlementAppliedAt,
            update.CorrelationId);
    }

    private Task<Result> RecordAuditAsync(
        Invoice invoice,
        InvoiceSettlementUpdate update,
        string beforeJson,
        string afterJson,
        CancellationToken cancellationToken)
    {
        AuditEntry audit = new()
        {
            EventType = InvoiceAuditEventTypes.InvoiceSettlementUpdated,
            Operation = AuditOperation.Update,
            EntityType = InvoiceAuditEventTypes.EntityType,
            EntityId = invoice.Id.ToString(),
            UserId = _currentUser.GetUserId(),
            Username = _currentUser.GetUsername(),
            OccurredAt = DateTimeOffset.UtcNow,
            CorrelationId = update.CorrelationId,
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            Reason = null
        };

        return _audit.RecordAsync(audit, cancellationToken);
    }

    /// <summary>
    /// Serializes the settlement snapshot for the audit trail. It includes the ordering token, so the trail
    /// shows WHICH event won (SDD-INV-001 §2.15 step 6).
    /// </summary>
    /// <param name="invoice">The invoice to snapshot.</param>
    /// <returns>The settlement snapshot as JSON.</returns>
    private static string SerializeSettlement(Invoice invoice)
    {
        return JsonSerializer.Serialize(new
        {
            invoice.Id,
            invoice.DocumentNumber,
            Status = invoice.Status.ToString(),
            invoice.GrossTotal,
            invoice.SettledAmount,
            SettlementStatus = invoice.SettlementStatus.ToString(),
            invoice.LastSettlementAppliedAt
        });
    }
}
