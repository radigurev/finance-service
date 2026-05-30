using Finance.EventLog.API.Interfaces;
using Finance.EventLog.DBModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Finance.EventLog.API.Services;

/// <summary>
/// Default <see cref="IEventLogRetentionService"/> that deletes <c>EventLogEntry</c> rows whose
/// <c>OccurredAt</c> precedes <c>now - EventLog:RetentionDays</c> (SDD-EVTLOG-001 §2.7). The delete is a
/// single set-based <c>ExecuteDeleteAsync</c>, keeping the operation cheap even when the daily backlog is
/// large. The cutoff is computed from <see cref="DateTimeOffset.UtcNow"/> at call time.
/// </summary>
public sealed class EventLogRetentionService : IEventLogRetentionService
{
    private readonly EventLogDbContext _db;
    private readonly EventLogRetentionOptions _options;

    /// <summary>Creates a new <see cref="EventLogRetentionService"/>.</summary>
    /// <param name="db">The EventLog database context.</param>
    /// <param name="options">The retention options carrying the configured window.</param>
    public EventLogRetentionService(EventLogDbContext db, IOptions<EventLogRetentionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(options);

        _db = db;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-_options.RetentionDays);

        return await _db.EventLogEntries
            .Where(entry => entry.OccurredAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
