using Finance.Journal.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Journal.DBModel.Configurations;

/// <summary>
/// EF Core Fluent-API configuration for the <see cref="JournalEntry"/> aggregate root (SDD-FIN-001 §2.1).
/// Maps to <c>journal.JournalEntries</c> with a sequential-GUID PK, a <c>rowversion</c> concurrency token,
/// the enum-as-string status column, and the composed line / status-history collections.
/// <para>Also carries the SDD-PAY-001 §2.5 duplicate-post backstop: the nullable source-document pair and the
/// UNIQUE FILTERED index <c>IX_JournalEntries_SourceDocument</c> admitting at most one <c>Posted</c> entry per
/// source document.</para>
/// </summary>
public sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    /// <summary>Configures the table, columns, indexes, and relationships for <see cref="JournalEntry"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("JournalEntries", schema: "journal");

        builder.HasKey(entry => entry.Id).HasName("PK_JournalEntries");

        builder.Property(entry => entry.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()");

        builder.Property(entry => entry.EntryNumber).HasMaxLength(40);
        builder.Property(entry => entry.EntryDate).IsRequired();
        builder.Property(entry => entry.Description).IsRequired().HasMaxLength(500);
        builder.Property(entry => entry.BaseCurrencyCode).IsRequired().HasMaxLength(3);

        builder.Property(entry => entry.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(entry => entry.ReversesEntryId);
        builder.Property(entry => entry.SourceDocumentType).HasMaxLength(40);
        builder.Property(entry => entry.SourceDocumentId);
        builder.Property(entry => entry.CorrelationId).IsRequired().HasMaxLength(100);
        builder.Property(entry => entry.CreatedAt).IsRequired().HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.Property(entry => entry.CreatedBy).IsRequired();
        builder.Property(entry => entry.PostedAt);
        builder.Property(entry => entry.PostedBy);
        builder.Property(entry => entry.RowVersion).IsRowVersion();

        builder.HasIndex(entry => entry.EntryNumber)
            .IsUnique()
            .HasFilter("[EntryNumber] IS NOT NULL")
            .HasDatabaseName("IX_JournalEntries_EntryNumber");
        builder.HasIndex(entry => entry.Status).HasDatabaseName("IX_JournalEntries_Status");
        builder.HasIndex(entry => entry.EntryDate).HasDatabaseName("IX_JournalEntries_EntryDate");
        builder.HasIndex(entry => entry.ReversesEntryId).HasDatabaseName("IX_JournalEntries_ReversesEntryId");

        builder.HasIndex(entry => new { entry.SourceDocumentType, entry.SourceDocumentId })
            .IsUnique()
            .HasFilter(
                "[SourceDocumentType] IS NOT NULL AND [SourceDocumentId] IS NOT NULL AND [Status] = 'Posted'")
            .HasDatabaseName("IX_JournalEntries_SourceDocument");

        builder.HasMany(entry => entry.Lines)
            .WithOne(line => line.JournalEntry)
            .HasForeignKey(line => line.JournalEntryId)
            .HasConstraintName("FK_JournalEntryLines_JournalEntries")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(entry => entry.StatusHistory)
            .WithOne(history => history.JournalEntry)
            .HasForeignKey(history => history.JournalEntryId)
            .HasConstraintName("FK_JournalEntryStatusHistory_JournalEntries")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(entry => entry.Lines).AutoInclude();
    }
}
