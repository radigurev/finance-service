using AutoMapper;
using Finance.EventLog.DBModel.Models;
using Finance.ServiceModel.EventLog;

namespace Finance.EventLog.API.Mapping;

/// <summary>
/// AutoMapper profile for the EventLog query API (SDD-EVTLOG-001 §2.4). Maps the persisted
/// <see cref="EventLogEntry"/> archive row to the read-only <see cref="EventLogEntryDto"/>.
/// </summary>
public sealed class EventLogMappingProfile : Profile
{
    /// <summary>Configures the mapping between <see cref="EventLogEntry"/> and <see cref="EventLogEntryDto"/>.</summary>
    public EventLogMappingProfile()
    {
        CreateMap<EventLogEntry, EventLogEntryDto>();
    }
}
