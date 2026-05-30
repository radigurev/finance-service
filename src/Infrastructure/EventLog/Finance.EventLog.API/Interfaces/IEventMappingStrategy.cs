using Finance.EventLog.DBModel.Models;
using Finance.ServiceModel.Events;

namespace Finance.EventLog.API.Interfaces;

/// <summary>
/// Strategy that maps a single inbound Finance domain event of type <typeparamref name="TEvent"/> to an
/// append-only <see cref="EventLogEntry"/> archive row (SDD-EVTLOG-001 §2.2). Exactly one strategy is
/// registered per event type via <c>services.AddScoped&lt;IEventMappingStrategy&lt;TEvent&gt;, TEventStrategy&gt;()</c>;
/// adding a new event type is therefore "new strategy class + new consumer + scoped registration" with no
/// change to existing strategies.
/// </summary>
/// <typeparam name="TEvent">The Finance domain-event contract this strategy maps.</typeparam>
public interface IEventMappingStrategy<TEvent>
    where TEvent : class, IFinanceEvent
{
    /// <summary>
    /// Maps <paramref name="message"/> to a new <see cref="EventLogEntry"/> with <c>EventId</c> set from the
    /// transport <paramref name="messageId"/>, <c>EventType</c> and <c>SourceService</c> describing the event,
    /// <c>OccurredAt</c> taken from the event, <c>ReceivedAt</c> set to <paramref name="receivedAt"/>,
    /// <c>CorrelationId</c> carried from the event, and <c>PayloadJson</c> the <c>System.Text.Json</c>
    /// serialization of the event. The serialization MUST tolerate unknown JSON properties so Warehouse / event
    /// schema evolution does not break consumption.
    /// </summary>
    /// <param name="message">The consumed event payload.</param>
    /// <param name="messageId">The inbound MassTransit message identifier (becomes <c>EventLogEntry.EventId</c>).</param>
    /// <param name="receivedAt">The instant at which EventLog consumed the event (becomes <c>ReceivedAt</c>).</param>
    /// <returns>A populated, not-yet-persisted <see cref="EventLogEntry"/>.</returns>
    EventLogEntry MapToEntry(TEvent message, Guid messageId, DateTimeOffset receivedAt);
}
