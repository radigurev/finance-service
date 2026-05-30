namespace Finance.EventLog.API.Services;

/// <summary>
/// Strongly-typed retention settings bound from the <c>EventLog</c> configuration section
/// (SDD-EVTLOG-001 §2.7). <see cref="RetentionDays"/> defaults to 90 days.
/// </summary>
public sealed class EventLogRetentionOptions
{
    /// <summary>The configuration section name these options bind from.</summary>
    public const string SectionName = "EventLog";

    /// <summary>The number of days an <c>EventLogEntry</c> is retained before the daily job deletes it.</summary>
    public int RetentionDays { get; set; } = 90;
}
