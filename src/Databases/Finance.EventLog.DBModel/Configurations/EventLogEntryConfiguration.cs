using Finance.EventLog.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.EventLog.DBModel.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="EventLogEntry"/> entity (SDD-EVTLOG-001 §2.0). The table
/// lives in the <c>eventlog</c> schema; <c>EventId</c> carries a unique index (so a duplicate insert that
/// slips past the idempotency filter fails rather than creating a second row) and <c>CorrelationId</c>
/// carries a non-unique index for the "show me everything in this trace" query (SDD-EVTLOG-001 §2.5).
/// Mapping uses Fluent API only — no Data Annotations.
/// </summary>
public sealed class EventLogEntryConfiguration : IEntityTypeConfiguration<EventLogEntry>
{
    /// <summary>Configures the table, columns, and indexes for <see cref="EventLogEntry"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<EventLogEntry> builder)
    {
        builder.ToTable("EventLogEntries", schema: "eventlog");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventId).IsRequired();
        builder.Property(e => e.EventType).IsRequired().HasMaxLength(200);
        builder.Property(e => e.SourceService).IsRequired().HasMaxLength(100);
        builder.Property(e => e.OccurredAt).IsRequired();
        builder.Property(e => e.ReceivedAt).IsRequired().HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.Property(e => e.CorrelationId).IsRequired().HasMaxLength(100);
        builder.Property(e => e.PayloadJson).IsRequired();

        builder.HasIndex(e => e.EventId).IsUnique();
        builder.HasIndex(e => e.CorrelationId);
        builder.HasIndex(e => e.OccurredAt);
    }
}
