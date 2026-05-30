using Finance.EventLog.API.Interfaces;
using Finance.EventLog.DBModel;
using Finance.ServiceModel.Events.Accounts;
using Microsoft.Extensions.Logging;

namespace Finance.EventLog.API.Consumers;

/// <summary>
/// MassTransit consumer that archives <see cref="AccountCreatedEvent"/> messages into the event-log
/// (SDD-EVTLOG-001 §2.1). Wrapped by <c>UseFinanceIdempotency()</c> so replays never duplicate a row.
/// </summary>
public sealed class AccountCreatedEventConsumer : EventLogConsumerBase<AccountCreatedEvent>
{
    /// <summary>Creates a new <see cref="AccountCreatedEventConsumer"/>.</summary>
    /// <param name="db">The EventLog database context.</param>
    /// <param name="strategy">The mapping strategy for <see cref="AccountCreatedEvent"/>.</param>
    /// <param name="logger">The consumer logger.</param>
    public AccountCreatedEventConsumer(
        EventLogDbContext db,
        IEventMappingStrategy<AccountCreatedEvent> strategy,
        ILogger<AccountCreatedEventConsumer> logger)
        : base(db, strategy, logger)
    {
    }
}
