namespace Finance.Infrastructure.Audit.Models;

/// <summary>
/// The kind of change an <see cref="AuditEntry"/> describes (SDD-AUDIT-001 §3). It drives the
/// <c>BeforeJson</c> invariant: <see cref="Create"/> entries MUST have a <c>null</c> <c>BeforeJson</c>,
/// while <see cref="Update"/>, <see cref="Delete"/>, and <see cref="StateChange"/> entries MUST carry a
/// non-empty pre-change snapshot.
/// </summary>
public enum AuditOperation
{
    /// <summary>A new aggregate was created; <c>BeforeJson</c> MUST be <c>null</c>.</summary>
    Create,

    /// <summary>An existing aggregate was modified; <c>BeforeJson</c> MUST be non-empty.</summary>
    Update,

    /// <summary>An existing aggregate was deleted; <c>BeforeJson</c> MUST be non-empty.</summary>
    Delete,

    /// <summary>An aggregate transitioned workflow state; <c>BeforeJson</c> MUST be non-empty.</summary>
    StateChange
}
