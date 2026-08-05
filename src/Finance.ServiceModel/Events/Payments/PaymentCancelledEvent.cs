namespace Finance.ServiceModel.Events.Payments;

/// <summary>
/// Domain event published through the transactional outbox when a draft payment is cancelled (voided)
/// (SDD-PAY-001 §2.6, §2.14; SDD-INFRA-006 §2.2). Cancel is legal from <c>Draft</c> ONLY, so no gapless
/// document number is ever released, recycled, or reassigned.
/// <para>Implements <see cref="IFinanceEvent"/>: <see cref="CorrelationId"/> is sourced from
/// <c>ICorrelationIdAccessor.Get()</c>, <see cref="MessageId"/> is a new GUID at construction, and
/// <see cref="OccurredAt"/> is <c>DateTimeOffset.UtcNow</c>.</para>
/// </summary>
public sealed record PaymentCancelledEvent : IFinanceEvent
{
    /// <inheritdoc />
    public required Guid MessageId { get; init; }

    /// <inheritdoc />
    public required string CorrelationId { get; init; }

    /// <inheritdoc />
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The sequential-GUID identifier of the cancelled payment.</summary>
    public required Guid PaymentId { get; init; }

    /// <summary>
    /// The payment's own document number. ALWAYS <c>null</c> in v1 because cancel is legal only from
    /// <c>Draft</c> and a draft carries no number; the property stays nullable so a future
    /// void-after-confirm feature needs no event change (SDD-PAY-001 §2.14).
    /// </summary>
    public string? DocumentNumber { get; init; }

    /// <summary>The mandatory operator-supplied reason for the cancellation.</summary>
    public required string Reason { get; init; }
}
