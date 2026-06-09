namespace Finance.ServiceModel.Events.Invoices;

/// <summary>
/// Domain event published through the transactional outbox when an invoice is cancelled (voided)
/// (SDD-INV-001 §2.6, §2.11; SDD-INFRA-006 §2.2). A cancelled confirmed invoice keeps (never recycles) its
/// document number.
/// <para>Implements <see cref="IFinanceEvent"/>: <see cref="CorrelationId"/> is sourced from
/// <c>ICorrelationIdAccessor.Get()</c>, <see cref="MessageId"/> is a new GUID at construction, and
/// <see cref="OccurredAt"/> is <c>DateTimeOffset.UtcNow</c>.</para>
/// </summary>
public sealed record InvoiceCancelledEvent : IFinanceEvent
{
    /// <inheritdoc />
    public required Guid MessageId { get; init; }

    /// <inheritdoc />
    public required string CorrelationId { get; init; }

    /// <inheritdoc />
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The sequential-GUID identifier of the cancelled invoice.</summary>
    public required Guid InvoiceId { get; init; }

    /// <summary>The document number of the cancelled invoice; <c>null</c> when a draft was cancelled.</summary>
    public string? DocumentNumber { get; init; }

    /// <summary>The mandatory operator-supplied reason for the cancellation.</summary>
    public required string Reason { get; init; }
}
