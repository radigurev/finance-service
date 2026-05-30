using AutoMapper;
using Finance.EventLog.API.Mapping;
using Finance.EventLog.API.Services;
using Finance.EventLog.DBModel;
using Finance.EventLog.DBModel.Models;

namespace Finance.EventLog.API.Tests.Fixtures;

/// <summary>
/// Assembles an <see cref="EventQueryService"/> over a SQLite in-memory context with a real AutoMapper
/// configured from <see cref="EventLogMappingProfile"/> for the EventLog query unit tests
/// (SDD-EVTLOG-001 §6). Also seeds deterministic archive rows.
/// </summary>
public static class EventQueryServiceTestHarness
{
    /// <summary>Builds the query service over the supplied context.</summary>
    /// <param name="db">The SQLite-backed event-log context.</param>
    /// <returns>A wired <see cref="EventQueryService"/>.</returns>
    public static EventQueryService Build(EventLogDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        IMapper mapper = new MapperConfiguration(cfg => cfg.AddProfile<EventLogMappingProfile>())
            .CreateMapper();

        return new EventQueryService(db, mapper, new StubCorrelationIdAccessor());
    }

    /// <summary>Builds and persists an <see cref="EventLogEntry"/> with the supplied descriptors.</summary>
    /// <param name="eventType">The archived event type name.</param>
    /// <param name="correlationId">The correlation id of the archived event.</param>
    /// <param name="occurredAt">The originating change instant.</param>
    /// <returns>The constructed entry (not yet attached to a context).</returns>
    public static EventLogEntry Entry(string eventType, string correlationId, DateTimeOffset occurredAt)
    {
        return new EventLogEntry
        {
            EventId = Guid.NewGuid(),
            EventType = eventType,
            SourceService = EventLogSourceServices.Accounts,
            OccurredAt = occurredAt,
            ReceivedAt = occurredAt,
            CorrelationId = correlationId,
            PayloadJson = "{}"
        };
    }
}
