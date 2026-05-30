using System.Text.Json;
using Finance.EventLog.API.Interfaces;
using Finance.EventLog.DBModel.Models;
using Finance.ServiceModel.Events;

namespace Finance.EventLog.API.Mapping;

/// <summary>
/// Shared base for the per-event <see cref="IEventMappingStrategy{TEvent}"/> implementations
/// (SDD-EVTLOG-001 §2.2). It builds the <see cref="EventLogEntry"/> from the transport identifiers and the
/// event's own <see cref="IFinanceEvent.OccurredAt"/> / <see cref="IFinanceEvent.CorrelationId"/>, and
/// serializes the full event into <c>PayloadJson</c> with the tolerant <see cref="EventLogJsonOptions"/>.
/// Concrete strategies supply only the archived <see cref="EventType"/> and originating
/// <see cref="SourceService"/>, so adding a new event type is a one-class change.
/// </summary>
/// <typeparam name="TEvent">The Finance domain-event contract this strategy maps.</typeparam>
public abstract class EventLogMappingStrategyBase<TEvent> : IEventMappingStrategy<TEvent>
    where TEvent : class, IFinanceEvent
{
    /// <summary>The CLR type name recorded in <c>EventLogEntry.EventType</c> for this strategy.</summary>
    protected abstract string EventType { get; }

    /// <summary>The originating Finance service recorded in <c>EventLogEntry.SourceService</c>.</summary>
    protected abstract string SourceService { get; }

    /// <inheritdoc />
    public EventLogEntry MapToEntry(TEvent message, Guid messageId, DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(message);

        string payloadJson = JsonSerializer.Serialize(message, EventLogJsonOptions.Default);

        return new EventLogEntry
        {
            EventId = messageId,
            EventType = EventType,
            SourceService = SourceService,
            OccurredAt = message.OccurredAt,
            ReceivedAt = receivedAt,
            CorrelationId = message.CorrelationId,
            PayloadJson = payloadJson
        };
    }
}
