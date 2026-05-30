using Finance.EventLog.API.Interfaces;
using Finance.EventLog.DBModel;
using Finance.ServiceModel.Events.Nomenclature;
using Microsoft.Extensions.Logging;

namespace Finance.EventLog.API.Consumers;

/// <summary>
/// MassTransit consumer that archives <see cref="CurrencyDeactivatedEvent"/> messages into the event-log
/// (SDD-EVTLOG-001 §2.1). Wrapped by <c>UseFinanceIdempotency()</c> so replays never duplicate a row.
/// </summary>
public sealed class CurrencyDeactivatedEventConsumer : EventLogConsumerBase<CurrencyDeactivatedEvent>
{
    /// <summary>Creates a new <see cref="CurrencyDeactivatedEventConsumer"/>.</summary>
    /// <param name="db">The EventLog database context.</param>
    /// <param name="strategy">The mapping strategy for <see cref="CurrencyDeactivatedEvent"/>.</param>
    /// <param name="logger">The consumer logger.</param>
    public CurrencyDeactivatedEventConsumer(
        EventLogDbContext db,
        IEventMappingStrategy<CurrencyDeactivatedEvent> strategy,
        ILogger<CurrencyDeactivatedEventConsumer> logger)
        : base(db, strategy, logger)
    {
    }
}
