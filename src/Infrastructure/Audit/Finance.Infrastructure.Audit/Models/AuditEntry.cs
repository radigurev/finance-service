namespace Finance.Infrastructure.Audit.Models;

/// <summary>
/// Caller-facing audit record passed to <c>IAuditService.RecordAsync</c> (SDD-AUDIT-001 §2.3).
/// <c>AuditService</c> maps it onto the <c>OperationsEvent</c> EF entity persisted through the
/// ambient <c>IAuditDbContext</c>. The shape is stable: changes require a major version bump and
/// a compliance-reviewed migration plan (SDD-AUDIT-001 §5).
/// </summary>
public sealed record AuditEntry
{
    /// <summary>The domain event type (e.g. "JournalEntryPosted", "InvoiceCancelled").</summary>
    public required string EventType { get; init; }

    /// <summary>
    /// The kind of change being audited. Drives the <c>BeforeJson</c> invariant (SDD-AUDIT-001 §3):
    /// <c>Create</c> requires a <c>null</c> <see cref="BeforeJson"/>; <c>Update</c>, <c>Delete</c>, and
    /// <c>StateChange</c> require a non-empty one.
    /// </summary>
    public required AuditOperation Operation { get; init; }

    /// <summary>The aggregate type the event applies to (e.g. "JournalEntry", "Invoice").</summary>
    public required string EntityType { get; init; }

    /// <summary>Stringified primary key of the affected aggregate.</summary>
    public required string EntityId { get; init; }

    /// <summary>Identifier of the user who performed the change.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Display name of the user who performed the change.</summary>
    public required string Username { get; init; }

    /// <summary>Time-zone-aware moment the change occurred.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Ambient correlation identifier tying the entry to the originating request.</summary>
    public required string CorrelationId { get; init; }

    /// <summary>Pre-change JSON snapshot; <c>null</c> on create, non-null on update / delete / state-change.</summary>
    public string? BeforeJson { get; init; }

    /// <summary>Post-change JSON snapshot.</summary>
    public required string AfterJson { get; init; }

    /// <summary>Operator-supplied "why" for high-sensitivity operations; <c>null</c> otherwise.</summary>
    public string? Reason { get; init; }
}
