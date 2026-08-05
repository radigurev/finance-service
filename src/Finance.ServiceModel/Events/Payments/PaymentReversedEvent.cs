namespace Finance.ServiceModel.Events.Payments;

/// <summary>
/// Domain event published through the transactional outbox when a posted payment is reversed
/// (SDD-PAY-001 §2.7, §2.14; SDD-INFRA-006 §2.2). An idempotent Journal-side consumer loads the linked entry,
/// takes its base64 <c>RowVersion</c>, and reverses it through the shipped
/// <c>IJournalEntryService.ReverseAsync</c> path — a sign-flipped new entry, never an UPDATE. A linked entry
/// already in <c>Reversed</c> is a success no-op.
/// <para>Implements <see cref="IFinanceEvent"/>: <see cref="CorrelationId"/> is sourced from
/// <c>ICorrelationIdAccessor.Get()</c>, <see cref="MessageId"/> is a new GUID at construction, and
/// <see cref="OccurredAt"/> is <c>DateTimeOffset.UtcNow</c>.</para>
/// </summary>
public sealed record PaymentReversedEvent : IFinanceEvent
{
    /// <inheritdoc />
    public required Guid MessageId { get; init; }

    /// <inheritdoc />
    public required string CorrelationId { get; init; }

    /// <inheritdoc />
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The sequential-GUID identifier of the reversed payment.</summary>
    public required Guid PaymentId { get; init; }

    /// <summary>The payment's own gapless document number (retained; never recycled on reversal).</summary>
    public required string DocumentNumber { get; init; }

    /// <summary>The journal entry linked to the payment, which the Journal side reverses.</summary>
    public required Guid JournalEntryId { get; init; }

    /// <summary>The mandatory operator-supplied reason for the reversal.</summary>
    public required string Reason { get; init; }
}
