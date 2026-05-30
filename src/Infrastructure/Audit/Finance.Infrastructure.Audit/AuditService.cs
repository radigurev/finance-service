using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Infrastructure.Audit.Entities;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Infrastructure.Audit.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Finance.Infrastructure.Audit;

/// <summary>
/// Write-path audit service (SDD-AUDIT-001 §2.4) backed by a service DbContext that implements
/// <see cref="IAuditDbContext"/>. Maps an <see cref="AuditEntry"/> onto an <see cref="OperationsEvent"/>
/// and adds it to the ambient context within the caller's open transaction. By default it does not
/// commit — the caller owns the transaction boundary and must persist the audit row before any
/// MassTransit outbox row (audit-first ordering, SDD-AUDIT-001 §2.4).
/// </summary>
/// <typeparam name="TContext">The service DbContext type that owns the audit set.</typeparam>
public sealed class AuditService<TContext> : IAuditService
    where TContext : DbContext, IAuditDbContext
{
    private readonly TContext _context;
    private readonly ILogger<AuditService<TContext>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditService{TContext}"/> class.
    /// </summary>
    /// <param name="context">The ambient service DbContext that owns the audit set.</param>
    /// <param name="logger">Structured logger for audit-write diagnostics.</param>
    public AuditService(TContext context, ILogger<AuditService<TContext>> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The optional <paramref name="saveChanges"/> parameter controls the transaction boundary: when
    /// <c>false</c> (the default) the audit row is added to the ambient context but NOT committed — the
    /// caller must include it in their own <c>SaveChanges</c> so the audit row commits atomically with the
    /// change it describes and before any MassTransit outbox row (audit-first ordering, SDD-AUDIT-001 §2.4).
    /// When <c>true</c> the service persists the row itself.
    /// </remarks>
    public async Task<Result> RecordAsync(AuditEntry entry, CancellationToken cancellationToken, bool saveChanges = false)
    {
        ArgumentNullException.ThrowIfNull(entry);

        EnforceBeforeJsonInvariant(entry);

        if (SensitiveAuditEventTypes.RequiresReason(entry.EventType) && string.IsNullOrWhiteSpace(entry.Reason))
        {
            _logger.LogWarning(
                "Audit entry for sensitive event {EventType} on {EntityType} {EntityId} rejected: reason required.",
                entry.EventType,
                entry.EntityType,
                entry.EntityId);

            return Result.Failure(
                AuditErrorCodes.AUDIT_REASON_REQUIRED,
                $"A reason is required for the high-sensitivity audit event '{entry.EventType}'.");
        }

        OperationsEvent operationsEvent = MapToEntity(entry);
        await _context.OperationsEvents.AddAsync(operationsEvent, cancellationToken).ConfigureAwait(false);

        if (saveChanges)
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Recorded audit event {EventType} for {EntityType} {EntityId} (correlationId {CorrelationId}).",
            entry.EventType,
            entry.EntityType,
            entry.EntityId,
            entry.CorrelationId);

        return Result.Success();
    }

    /// <summary>
    /// Enforces the SDD-AUDIT-001 §3 <c>BeforeJson</c> invariant: a <see cref="AuditOperation.Create"/>
    /// entry MUST have a <c>null</c> <c>BeforeJson</c>, while <see cref="AuditOperation.Update"/>,
    /// <see cref="AuditOperation.Delete"/>, and <see cref="AuditOperation.StateChange"/> entries MUST carry
    /// a non-empty one. A violation throws before any row is written.
    /// </summary>
    /// <param name="entry">The audit entry to validate.</param>
    /// <exception cref="ArgumentException">Thrown when the <c>BeforeJson</c> invariant is violated.</exception>
    private static void EnforceBeforeJsonInvariant(AuditEntry entry)
    {
        if (entry.Operation == AuditOperation.Create)
        {
            if (entry.BeforeJson is not null)
            {
                throw new ArgumentException("BeforeJson must be null for a Create audit entry", nameof(entry));
            }

            return;
        }

        if (string.IsNullOrEmpty(entry.BeforeJson))
        {
            throw new ArgumentException(
                $"BeforeJson must be non-empty for a {entry.Operation} audit entry", nameof(entry));
        }
    }

    private static OperationsEvent MapToEntity(AuditEntry entry) => new()
    {
        EventType = entry.EventType,
        EntityType = entry.EntityType,
        EntityId = entry.EntityId,
        UserId = entry.UserId,
        Username = entry.Username,
        OccurredAt = entry.OccurredAt,
        CorrelationId = entry.CorrelationId,
        BeforeJson = entry.BeforeJson,
        AfterJson = entry.AfterJson,
        Reason = entry.Reason,
    };
}
