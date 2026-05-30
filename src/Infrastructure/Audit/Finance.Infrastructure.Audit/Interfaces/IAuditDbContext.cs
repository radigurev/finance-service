using Finance.Infrastructure.Audit.Entities;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Audit.Interfaces;

/// <summary>
/// Seam implemented by every service DbContext that participates in the audit trail (SDD-AUDIT-001 §2.3).
/// Exposes the append-only <see cref="OperationsEvent"/> set so <c>AuditService&lt;TContext&gt;</c> can
/// write audit rows into the ambient context within the same transaction as the change they describe.
/// </summary>
public interface IAuditDbContext
{
    /// <summary>The append-only audit-event set mapped to the <c>audit</c> schema.</summary>
    DbSet<OperationsEvent> OperationsEvents { get; }
}
