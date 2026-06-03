using Finance.Periods.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Periods.DBModel.Configurations;

/// <summary>
/// EF Core Fluent-API configuration for the <see cref="FiscalPeriod"/> aggregate root (SDD-FIN-004 §3).
/// Maps to <c>periods.FiscalPeriods</c> with an INT IDENTITY PK, a <c>rowversion</c> concurrency token,
/// the enum-as-string status column, the unique <c>(FiscalYear, PeriodNumber)</c> index, and the composed
/// status-history collection.
/// </summary>
public sealed class FiscalPeriodConfiguration : IEntityTypeConfiguration<FiscalPeriod>
{
    /// <summary>Configures the table, columns, indexes, and relationships for <see cref="FiscalPeriod"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<FiscalPeriod> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("FiscalPeriods", schema: "periods");

        builder.HasKey(period => period.Id).HasName("PK_FiscalPeriods");

        builder.Property(period => period.FiscalYear).IsRequired();
        builder.Property(period => period.PeriodNumber).IsRequired();
        builder.Property(period => period.Name).IsRequired().HasMaxLength(100);
        builder.Property(period => period.StartDate).IsRequired();
        builder.Property(period => period.EndDate).IsRequired();

        builder.Property(period => period.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(period => period.ClosedAt);
        builder.Property(period => period.ClosedBy);
        builder.Property(period => period.ReopenedAt);
        builder.Property(period => period.ReopenedBy);
        builder.Property(period => period.CorrelationId).IsRequired().HasMaxLength(100);
        builder.Property(period => period.CreatedAt).IsRequired().HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.Property(period => period.CreatedBy).IsRequired();
        builder.Property(period => period.RowVersion).IsRowVersion();

        builder.HasIndex(period => new { period.FiscalYear, period.PeriodNumber })
            .IsUnique()
            .HasDatabaseName("IX_FiscalPeriods_FiscalYear_PeriodNumber");
        builder.HasIndex(period => period.Status).HasDatabaseName("IX_FiscalPeriods_Status");
        builder.HasIndex(period => new { period.StartDate, period.EndDate })
            .HasDatabaseName("IX_FiscalPeriods_StartDate_EndDate");

        builder.HasMany(period => period.StatusHistory)
            .WithOne(history => history.FiscalPeriod)
            .HasForeignKey(history => history.FiscalPeriodId)
            .HasConstraintName("FK_FiscalPeriodStatusHistory_FiscalPeriods")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
