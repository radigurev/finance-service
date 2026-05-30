using Finance.ServiceModel.Events.Accounts;

namespace Finance.EventLog.API.Mapping;

/// <summary>
/// Maps an inbound <see cref="AccountCreatedEvent"/> to an <c>EventLogEntry</c> archive row
/// (SDD-EVTLOG-001 §2.2). The originating service is the Accounts microservice.
/// </summary>
public sealed class AccountCreatedEventMappingStrategy : EventLogMappingStrategyBase<AccountCreatedEvent>
{
    /// <inheritdoc />
    protected override string EventType => nameof(AccountCreatedEvent);

    /// <inheritdoc />
    protected override string SourceService => EventLogSourceServices.Accounts;
}
