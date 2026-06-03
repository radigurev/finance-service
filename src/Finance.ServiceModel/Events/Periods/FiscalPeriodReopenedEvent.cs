namespace Finance.ServiceModel.Events.Periods;

/// <summary>
/// Domain event published through the transactional outbox when a closed fiscal period is reopened
/// (SDD-FIN-004 §2.9, SDD-INFRA-006 §2.2). Consumers MUST key off <c>(FiscalYear, PeriodNumber)</c> rather
/// than the internal surrogate <see cref="FiscalPeriodId"/>.
/// <para>Implements <see cref="IFinanceEvent"/>: <see cref="CorrelationId"/> is sourced from
/// <c>ICorrelationIdAccessor.Get()</c>, <see cref="MessageId"/> is a new GUID at construction, and
/// <see cref="OccurredAt"/> is <c>DateTimeOffset.UtcNow</c>.</para>
/// </summary>
public sealed record FiscalPeriodReopenedEvent : IFinanceEvent
{
    /// <inheritdoc />
    public required Guid MessageId { get; init; }

    /// <inheritdoc />
    public required string CorrelationId { get; init; }

    /// <inheritdoc />
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The internal surrogate identifier of the reopened period (not externally stable).</summary>
    public required int FiscalPeriodId { get; init; }

    /// <summary>The accounting year of the reopened period (natural-key component).</summary>
    public required int FiscalYear { get; init; }

    /// <summary>The 1-based period ordinal of the reopened period (natural-key component).</summary>
    public required int PeriodNumber { get; init; }

    /// <summary>The first instant of the reopened period (inclusive).</summary>
    public required DateTimeOffset StartDate { get; init; }

    /// <summary>The last instant of the reopened period (inclusive).</summary>
    public required DateTimeOffset EndDate { get; init; }

    /// <summary>The mandatory operator-supplied reason for the reopen.</summary>
    public required string Reason { get; init; }
}
