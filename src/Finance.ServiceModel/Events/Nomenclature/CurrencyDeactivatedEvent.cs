namespace Finance.ServiceModel.Events.Nomenclature;

/// <summary>
/// Domain event published through the transactional outbox when a currency is deactivated (the
/// <c>IsActive</c> transition from <c>true</c> to <c>false</c> via update) — the only retirement path,
/// since hard delete is forbidden (SDD-NOM-001 §2.1, SDD-INFRA-006 §2.2). Deactivation MUST carry a
/// reason in the audit trail (SDD-AUDIT-001).
/// <para>Implements <see cref="IFinanceEvent"/>: <see cref="CorrelationId"/> is sourced from
/// <c>ICorrelationIdAccessor.Get()</c>, <see cref="MessageId"/> is a new GUID at construction, and
/// <see cref="OccurredAt"/> is <c>DateTimeOffset.UtcNow</c>.</para>
/// </summary>
public sealed record CurrencyDeactivatedEvent : IFinanceEvent
{
    /// <inheritdoc />
    public required Guid MessageId { get; init; }

    /// <inheritdoc />
    public required string CorrelationId { get; init; }

    /// <inheritdoc />
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Surrogate identifier of the deactivated currency.</summary>
    public required int CurrencyId { get; init; }

    /// <summary>ISO 4217 alphabetic code of the deactivated currency.</summary>
    public required string IsoCode { get; init; }

    /// <summary>Human-readable currency name.</summary>
    public required string Name { get; init; }

    /// <summary>Optional display symbol.</summary>
    public string? Symbol { get; init; }

    /// <summary>Active flag after deactivation; always <c>false</c> for this event.</summary>
    public required bool IsActive { get; init; }
}
