using Finance.Journal.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Journal.DBModel.Configurations;

/// <summary>
/// EF Core Fluent-API configuration for the append-only <see cref="JournalEntryStatusHistory"/> entity
/// (SDD-FIN-002 §2.4, SDD-INFRA-008 §2.4).
/// </summary>
public sealed class JournalEntryStatusHistoryConfiguration : IEntityTypeConfiguration<JournalEntryStatusHistory>
{
    /// <summary>Configures the table, columns, and indexes for <see cref="JournalEntryStatusHistory"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<JournalEntryStatusHistory> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("JournalEntryStatusHistory", schema: "journal");

        builder.HasKey(history => history.Id).HasName("PK_JournalEntryStatusHistory");

        builder.Property(history => history.JournalEntryId).IsRequired();
        builder.Property(history => history.FromStatus).HasMaxLength(20);
        builder.Property(history => history.ToStatus).IsRequired().HasMaxLength(20);
        builder.Property(history => history.ChangedBy).IsRequired();
        builder.Property(history => history.ChangedAt).IsRequired().HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.Property(history => history.CorrelationId).IsRequired().HasMaxLength(100);
        builder.Property(history => history.Reason).HasMaxLength(1000);

        builder.HasIndex(history => history.JournalEntryId)
            .HasDatabaseName("IX_JournalEntryStatusHistory_JournalEntryId");
    }
}
