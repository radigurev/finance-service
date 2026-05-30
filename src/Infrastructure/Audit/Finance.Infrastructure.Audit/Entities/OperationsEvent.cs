namespace Finance.Infrastructure.Audit.Entities;

/// <summary>
/// Persistent, append-only audit row mapped to the dedicated <c>audit</c> schema (SDD-AUDIT-001 §2.3).
/// Captures the legally-meaningful "who, what, when, why" of every change to financial data.
/// Rows are INSERT-only; the per-service migration that applies INSERT-only DB grants and the
/// <c>audit</c> schema is deferred (Batch 4+ — SDD-AUDIT-001 §2.5).
/// </summary>
public sealed class OperationsEvent
{
    /// <summary>Surrogate identifier of the audit row.</summary>
    public long Id { get; set; }

    /// <summary>The domain event type (e.g. "JournalEntryPosted", "InvoiceCancelled").</summary>
    public required string EventType { get; set; }

    /// <summary>The aggregate type the event applies to (e.g. "JournalEntry", "Invoice").</summary>
    public required string EntityType { get; set; }

    /// <summary>Stringified primary key of the affected aggregate.</summary>
    public required string EntityId { get; set; }

    /// <summary>Identifier of the user who performed the change.</summary>
    public required Guid UserId { get; set; }

    /// <summary>Display name of the user who performed the change.</summary>
    public required string Username { get; set; }

    /// <summary>Time-zone-aware moment the change occurred.</summary>
    public required DateTimeOffset OccurredAt { get; set; }

    /// <summary>Ambient correlation identifier tying the audit row to the originating request.</summary>
    public required string CorrelationId { get; set; }

    /// <summary>Pre-change JSON snapshot; <c>null</c> for create events.</summary>
    public string? BeforeJson { get; set; }

    /// <summary>Post-change JSON snapshot.</summary>
    public required string AfterJson { get; set; }

    /// <summary>Operator-supplied "why" for high-sensitivity operations; <c>null</c> otherwise.</summary>
    public string? Reason { get; set; }
}
