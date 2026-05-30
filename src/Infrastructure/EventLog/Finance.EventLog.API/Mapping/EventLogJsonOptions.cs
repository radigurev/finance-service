using System.Text.Json;
using System.Text.Json.Serialization;

namespace Finance.EventLog.API.Mapping;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> used by every <see cref="EventLogMappingStrategyBase{TEvent}"/>
/// to serialize an inbound event into <c>EventLogEntry.PayloadJson</c> (SDD-EVTLOG-001 §2.2). Unknown JSON
/// members are ignored on read so Warehouse / event schema evolution (an added property) cannot break the
/// round-trip, and enums are written as strings for human-readable archive payloads.
/// </summary>
public static class EventLogJsonOptions
{
    /// <summary>The tolerant serializer options applied to every archived event payload.</summary>
    public static readonly JsonSerializerOptions Default = CreateDefault();

    /// <summary>Builds the tolerant serializer options (ignore unknown members, string enums).</summary>
    /// <returns>The configured <see cref="JsonSerializerOptions"/>.</returns>
    private static JsonSerializerOptions CreateDefault()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
