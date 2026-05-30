using Finance.ServiceModel.Events.Nomenclature;

namespace Finance.EventLog.API.Mapping;

/// <summary>
/// Maps an inbound <see cref="CurrencyCreatedEvent"/> to an <c>EventLogEntry</c> archive row
/// (SDD-EVTLOG-001 §2.2). The originating service is the Nomenclature microservice.
/// </summary>
public sealed class CurrencyCreatedEventMappingStrategy : EventLogMappingStrategyBase<CurrencyCreatedEvent>
{
    /// <inheritdoc />
    protected override string EventType => nameof(CurrencyCreatedEvent);

    /// <inheritdoc />
    protected override string SourceService => EventLogSourceServices.Nomenclature;
}
