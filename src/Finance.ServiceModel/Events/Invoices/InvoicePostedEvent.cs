namespace Finance.ServiceModel.Events.Invoices;

/// <summary>
/// Dedicated back-event published by the Journal service through its transactional outbox once it has
/// posted the journal entry for a confirmed invoice (SDD-INV-001 §2.5, §2.11; SDD-INFRA-006 §2.2). The
/// Invoice service's idempotent consumer matches by <see cref="InvoiceId"/>, links the journal entry, and
/// moves the invoice <c>Confirmed → Posted</c>. A dedicated event is used rather than the generic
/// <c>JournalEntryPostedEvent</c> (which is multi-purpose and already consumed by EventLog), so the
/// invoice→posting correlation is explicit and unambiguous.
/// <para>Implements <see cref="IFinanceEvent"/>: <see cref="CorrelationId"/> carries the originating
/// invoice correlation, <see cref="MessageId"/> is a new GUID at construction, and <see cref="OccurredAt"/>
/// is <c>DateTimeOffset.UtcNow</c>.</para>
/// </summary>
public sealed record InvoicePostedEvent : IFinanceEvent
{
    /// <inheritdoc />
    public required Guid MessageId { get; init; }

    /// <inheritdoc />
    public required string CorrelationId { get; init; }

    /// <inheritdoc />
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The sequential-GUID identifier of the invoice whose posting completed.</summary>
    public required Guid InvoiceId { get; init; }

    /// <summary>The sequential-GUID identifier of the journal entry posted for the invoice.</summary>
    public required Guid JournalEntryId { get; init; }

    /// <summary>The gapless document number of the posted journal entry.</summary>
    public required string JournalEntryNumber { get; init; }
}
