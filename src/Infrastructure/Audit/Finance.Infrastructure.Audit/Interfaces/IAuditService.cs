using Finance.Common.Results;
using Finance.Infrastructure.Audit.Models;

namespace Finance.Infrastructure.Audit.Interfaces;

/// <summary>
/// Write-path audit service (SDD-AUDIT-001 §2.4). Records a legally-meaningful change into the
/// ambient <see cref="IAuditDbContext"/> within the caller's open transaction. The caller owns the
/// transaction boundary; the service does not commit unless the caller explicitly opts in.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Adds an <c>OperationsEvent</c> derived from <paramref name="entry"/> to the ambient
    /// <see cref="IAuditDbContext"/>. When <paramref name="saveChanges"/> is <c>false</c> (the default),
    /// the row is tracked but not persisted — the caller must include it in their own
    /// <c>SaveChanges</c> so the audit row commits atomically with the change it describes and is
    /// written before any MassTransit outbox row (SDD-AUDIT-001 §2.4). Returns
    /// <see cref="Result.Failure"/> with <c>AUDIT_REASON_REQUIRED</c> when a high-sensitivity event
    /// is recorded without a reason (SDD-AUDIT-001 §3).
    /// </summary>
    /// <param name="entry">The audit entry describing the change.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <param name="saveChanges">When <c>true</c>, the service persists the row itself; otherwise the caller owns the commit.</param>
    /// <returns>A success <see cref="Result"/>, or a failure when validation rejects the entry.</returns>
    Task<Result> RecordAsync(AuditEntry entry, CancellationToken cancellationToken, bool saveChanges = false);
}
