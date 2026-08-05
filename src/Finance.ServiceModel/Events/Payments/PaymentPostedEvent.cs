namespace Finance.ServiceModel.Events.Payments;

/// <summary>
/// Dedicated back-event published by the Journal service through its transactional outbox once it has posted
/// the journal entry for a confirmed payment (SDD-PAY-001 §2.5, §2.14; SDD-INFRA-006 §2.2). The Payments
/// service's idempotent consumer matches by <see cref="PaymentId"/>, links the journal entry, and moves the
/// payment <c>Confirmed → Posted</c>. A dedicated event is used rather than the generic
/// <c>JournalEntryPostedEvent</c> (which is multi-purpose and already consumed by EventLog), mirroring
/// <c>InvoicePostedEvent</c>.
/// <para>The payload deliberately carries NO payment document number — only the cross-document
/// <see cref="JournalEntryNumber"/> reference (SDD-PAY-001 §2.14 naming convention).</para>
/// <para>Implements <see cref="IFinanceEvent"/>: <see cref="CorrelationId"/> is propagated from the source
/// <see cref="PaymentConfirmedEvent"/> (never re-read from the ambient accessor), <see cref="MessageId"/> is
/// a new GUID at construction, and <see cref="OccurredAt"/> is <c>DateTimeOffset.UtcNow</c>.</para>
/// </summary>
public sealed record PaymentPostedEvent : IFinanceEvent
{
    /// <inheritdoc />
    public required Guid MessageId { get; init; }

    /// <inheritdoc />
    public required string CorrelationId { get; init; }

    /// <inheritdoc />
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The sequential-GUID identifier of the payment whose posting completed.</summary>
    public required Guid PaymentId { get; init; }

    /// <summary>The sequential-GUID identifier of the journal entry posted for the payment.</summary>
    public required Guid JournalEntryId { get; init; }

    /// <summary>The gapless document number of the posted journal entry.</summary>
    public required string JournalEntryNumber { get; init; }
}
