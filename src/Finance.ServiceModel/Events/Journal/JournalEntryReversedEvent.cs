namespace Finance.ServiceModel.Events.Journal;

/// <summary>
/// Domain event published through the transactional outbox when a posted journal entry is reversed
/// (SDD-FIN-002 §2.11, SDD-INFRA-006 §2.2).
/// <para>Implements <see cref="IFinanceEvent"/>: <see cref="CorrelationId"/> is sourced from
/// <c>ICorrelationIdAccessor.Get()</c>, <see cref="MessageId"/> is a new GUID at construction, and
/// <see cref="OccurredAt"/> is <c>DateTimeOffset.UtcNow</c>.</para>
/// </summary>
public sealed record JournalEntryReversedEvent : IFinanceEvent
{
    /// <inheritdoc />
    public required Guid MessageId { get; init; }

    /// <inheritdoc />
    public required string CorrelationId { get; init; }

    /// <inheritdoc />
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The identifier of the original entry that was reversed and moved to <c>Reversed</c>.</summary>
    public required Guid OriginalJournalEntryId { get; init; }

    /// <summary>The identifier of the new sign-flipped reversal entry (itself <c>Posted</c>).</summary>
    public required Guid ReversalJournalEntryId { get; init; }

    /// <summary>The gapless document number of the reversal entry.</summary>
    public required string ReversalEntryNumber { get; init; }

    /// <summary>The mandatory operator-supplied reason for the reversal.</summary>
    public required string Reason { get; init; }
}
