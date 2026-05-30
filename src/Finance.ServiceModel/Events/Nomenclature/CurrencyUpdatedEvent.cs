namespace Finance.ServiceModel.Events.Nomenclature;

/// <summary>
/// Domain event published through the transactional outbox when a currency is updated without being
/// deactivated (SDD-NOM-001 §2.1, §2.6, SDD-INFRA-006 §2.2). Re-activating a soft-deleted currency
/// also publishes this event.
/// <para>Implements <see cref="IFinanceEvent"/>: <see cref="CorrelationId"/> is sourced from
/// <c>ICorrelationIdAccessor.Get()</c>, <see cref="MessageId"/> is a new GUID at construction, and
/// <see cref="OccurredAt"/> is <c>DateTimeOffset.UtcNow</c>.</para>
/// </summary>
public sealed record CurrencyUpdatedEvent : IFinanceEvent
{
    /// <inheritdoc />
    public required Guid MessageId { get; init; }

    /// <inheritdoc />
    public required string CorrelationId { get; init; }

    /// <inheritdoc />
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Surrogate identifier of the updated currency.</summary>
    public required int CurrencyId { get; init; }

    /// <summary>ISO 4217 alphabetic code of the updated currency.</summary>
    public required string IsoCode { get; init; }

    /// <summary>Human-readable currency name after the update.</summary>
    public required string Name { get; init; }

    /// <summary>Optional display symbol after the update.</summary>
    public string? Symbol { get; init; }

    /// <summary>Whether the currency is active after the update.</summary>
    public required bool IsActive { get; init; }
}
