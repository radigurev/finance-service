using Finance.EventLog.DBModel.Configurations;
using Finance.EventLog.DBModel.Models;
using Microsoft.EntityFrameworkCore;

namespace Finance.EventLog.DBModel;

/// <summary>
/// EF Core database context for the EventLog operational archive service (SDD-EVTLOG-001). Owns the
/// <c>eventlog</c> schema containing the single append-only <see cref="EventLogEntry"/> table. EventLog
/// consumes domain events but does not publish, so there is no MassTransit transactional outbox here;
/// it is also exempt from SDD-AUDIT-001, so there is no <c>audit</c> schema.
/// </summary>
public sealed class EventLogDbContext : DbContext
{
    /// <summary>Creates a new <see cref="EventLogDbContext"/>.</summary>
    /// <param name="options">The context options supplied by DI or the design-time factory.</param>
    public EventLogDbContext(DbContextOptions<EventLogDbContext> options) : base(options)
    {
    }

    /// <summary>The append-only operational event archive rows owned by this service.</summary>
    public DbSet<EventLogEntry> EventLogEntries => Set<EventLogEntry>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("eventlog");
        modelBuilder.ApplyConfiguration(new EventLogEntryConfiguration());
    }
}
