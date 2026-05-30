using Finance.GenericFiltering.Attributes;

namespace Finance.EventLog.DBModel.Models;

/// <summary>
/// Append-only operational archive row for a MassTransit domain event consumed across the Finance
/// microservices (SDD-EVTLOG-001 §2.0). It is the entity earlier drafts called the "OperationsEvent row",
/// renamed <see cref="EventLogEntry"/> to stay distinct from the SDD-AUDIT-001 legal trail
/// (<c>audit.OperationsEvents</c>). Rows live in the <c>eventlog</c> schema, are never UPDATEd after insert,
/// and are exempt from SDD-AUDIT-001 audit writes (this is the operational archive, not the legal trail).
/// </summary>
public sealed class EventLogEntry
{
    /// <summary>Surrogate identifier (internal — the table primary key; <c>INT IDENTITY</c>).</summary>
    public int Id { get; set; }

    /// <summary>The inbound MassTransit <c>MessageId</c>; uniquely indexed so replays cannot duplicate a row.</summary>
    public Guid EventId { get; set; }

    /// <summary>The CLR type name of the consumed event (e.g. "AccountCreatedEvent"). Filterable and sortable.</summary>
    [Filterable]
    [Sortable]
    public required string EventType { get; set; }

    /// <summary>The originating Finance service (e.g. "finance-accounts-api"). Filterable and sortable.</summary>
    [Filterable]
    [Sortable]
    public required string SourceService { get; set; }

    /// <summary>The UTC instant at which the originating domain change occurred. Filterable and sortable.</summary>
    [Filterable]
    [Sortable]
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>The UTC instant at which EventLog consumed and persisted the event (<c>SYSDATETIMEOFFSET()</c> default).</summary>
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>The ambient correlation identifier carried by the event. Indexed, filterable, and sortable.</summary>
    [Filterable]
    [Sortable]
    public required string CorrelationId { get; set; }

    /// <summary>The <c>System.Text.Json</c> serialization of the full inbound event payload.</summary>
    public required string PayloadJson { get; set; }
}
