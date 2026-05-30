namespace Finance.EventLog.API.Interfaces;

/// <summary>
/// Removes expired rows from the operational event-log archive (SDD-EVTLOG-001 §2.7). Implementations
/// delete every <c>EventLogEntry</c> whose <c>OccurredAt</c> is older than the configured retention window
/// and report how many rows were removed so the hosted job can log a structured count.
/// </summary>
public interface IEventLogRetentionService
{
    /// <summary>
    /// Deletes every archive row older than the configured retention window and returns the deleted count.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The number of <c>EventLogEntry</c> rows deleted.</returns>
    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken);
}
