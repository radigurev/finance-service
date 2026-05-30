using Finance.EventLog.API.Interfaces;
using Finance.EventLog.DBModel;
using Finance.ServiceModel.Events.Accounts;
using Microsoft.Extensions.Logging;

namespace Finance.EventLog.API.Consumers;

/// <summary>
/// MassTransit consumer that archives <see cref="AccountDeactivatedEvent"/> messages into the event-log
/// (SDD-EVTLOG-001 §2.1). Wrapped by <c>UseFinanceIdempotency()</c> so replays never duplicate a row.
/// </summary>
public sealed class AccountDeactivatedEventConsumer : EventLogConsumerBase<AccountDeactivatedEvent>
{
    /// <summary>Creates a new <see cref="AccountDeactivatedEventConsumer"/>.</summary>
    /// <param name="db">The EventLog database context.</param>
    /// <param name="strategy">The mapping strategy for <see cref="AccountDeactivatedEvent"/>.</param>
    /// <param name="logger">The consumer logger.</param>
    public AccountDeactivatedEventConsumer(
        EventLogDbContext db,
        IEventMappingStrategy<AccountDeactivatedEvent> strategy,
        ILogger<AccountDeactivatedEventConsumer> logger)
        : base(db, strategy, logger)
    {
    }
}
