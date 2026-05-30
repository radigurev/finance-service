using Finance.EventLog.API.Interfaces;
using Finance.EventLog.DBModel;
using Finance.EventLog.DBModel.Models;
using Finance.ServiceModel.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Finance.EventLog.API.Consumers;

/// <summary>
/// Shared base for the per-event-type MassTransit consumers (SDD-EVTLOG-001 §2.1). It resolves the inbound
/// message identifier, delegates payload mapping to the per-type <see cref="IEventMappingStrategy{TEvent}"/>,
/// persists the resulting append-only <see cref="EventLogEntry"/>, and logs entry/exit with the inbound
/// <see cref="IFinanceEvent.CorrelationId"/> pushed onto the NLog scope (SDD-OBS-001). Idempotency is handled
/// transparently upstream by <c>UseFinanceIdempotency()</c> (SDD-INFRA-006), so a replay never reaches this
/// consumer twice; the unique index on <c>EventLogEntry.EventId</c> is the defence-in-depth backstop.
/// Consuming an event MUST NOT write an <c>audit.OperationsEvents</c> row — EventLog is the operational
/// archive, not the legal trail (SDD-EVTLOG-001 §2.1).
/// </summary>
/// <typeparam name="TEvent">The Finance domain-event contract this consumer archives.</typeparam>
public abstract class EventLogConsumerBase<TEvent> : IConsumer<TEvent>
    where TEvent : class, IFinanceEvent
{
    private readonly EventLogDbContext _db;
    private readonly IEventMappingStrategy<TEvent> _strategy;
    private readonly ILogger _logger;

    /// <summary>Initializes the consumer with its context, mapping strategy, and logger.</summary>
    /// <param name="db">The EventLog database context the archive row is written to.</param>
    /// <param name="strategy">The per-type strategy that maps the event to an <see cref="EventLogEntry"/>.</param>
    /// <param name="logger">The logger used for the structured entry/exit messages.</param>
    protected EventLogConsumerBase(
        EventLogDbContext db,
        IEventMappingStrategy<TEvent> strategy,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(logger);

        _db = db;
        _strategy = strategy;
        _logger = logger;
    }

    /// <summary>
    /// Maps the consumed event to an <see cref="EventLogEntry"/> and appends it to the archive, scoping the
    /// log to the inbound correlation id so the entry/exit pair is searchable in Loki/Jaeger.
    /// </summary>
    /// <param name="context">The MassTransit consume context carrying the event and its identifiers.</param>
    /// <returns>A task that completes when the archive row has been persisted.</returns>
    public async Task Consume(ConsumeContext<TEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TEvent message = context.Message;
        Guid messageId = context.MessageId ?? NewId.NextGuid();

        using (_logger.BeginScope(BuildLogScope(message.CorrelationId)))
        {
            _logger.LogInformation(
                "Archiving {EventType} message {MessageId} from correlation {CorrelationId}",
                typeof(TEvent).Name,
                messageId,
                message.CorrelationId);

            EventLogEntry entry = _strategy.MapToEntry(message, messageId, DateTimeOffset.UtcNow);
            await _db.EventLogEntries.AddAsync(entry, context.CancellationToken).ConfigureAwait(false);
            await _db.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Archived {EventType} message {MessageId} as event-log entry {EntryId}",
                typeof(TEvent).Name,
                messageId,
                entry.Id);
        }
    }

    private static Dictionary<string, object> BuildLogScope(string correlationId)
    {
        return new Dictionary<string, object> { ["CorrelationId"] = correlationId };
    }
}
