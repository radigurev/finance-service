namespace Finance.ServiceModel.EventLog;

/// <summary>
/// Read-only projection of an operational event-log archive row exposed by
/// <c>GET /api/v1/events</c> (SDD-EVTLOG-001 §2.4).
/// </summary>
public sealed record EventLogEntryDto
{
    /// <summary>Surrogate identifier of the archive row.</summary>
    public required int Id { get; init; }

    /// <summary>The inbound MassTransit message identifier of the archived event.</summary>
    public required Guid EventId { get; init; }

    /// <summary>The CLR type name of the consumed event (e.g. "AccountCreatedEvent").</summary>
    public required string EventType { get; init; }

    /// <summary>The originating Finance service (e.g. "finance-accounts-api").</summary>
    public required string SourceService { get; init; }

    /// <summary>The UTC instant at which the originating domain change occurred.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The UTC instant at which EventLog consumed and persisted the event.</summary>
    public required DateTimeOffset ReceivedAt { get; init; }

    /// <summary>The ambient correlation identifier carried by the event.</summary>
    public required string CorrelationId { get; init; }

    /// <summary>The JSON serialization of the full inbound event payload.</summary>
    public required string PayloadJson { get; init; }
}
