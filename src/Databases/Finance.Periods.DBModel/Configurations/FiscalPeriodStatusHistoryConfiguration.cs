using Finance.Periods.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Periods.DBModel.Configurations;

/// <summary>
/// EF Core Fluent-API configuration for the append-only <see cref="FiscalPeriodStatusHistory"/> entity
/// (SDD-FIN-004 §2.4, SDD-INFRA-008 §2.4).
/// </summary>
public sealed class FiscalPeriodStatusHistoryConfiguration : IEntityTypeConfiguration<FiscalPeriodStatusHistory>
{
    /// <summary>Configures the table, columns, and indexes for <see cref="FiscalPeriodStatusHistory"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<FiscalPeriodStatusHistory> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("FiscalPeriodStatusHistory", schema: "periods");

        builder.HasKey(history => history.Id).HasName("PK_FiscalPeriodStatusHistory");

        builder.Property(history => history.FiscalPeriodId).IsRequired();
        builder.Property(history => history.FromStatus).HasMaxLength(20);
        builder.Property(history => history.ToStatus).IsRequired().HasMaxLength(20);
        builder.Property(history => history.ChangedBy).IsRequired();
        builder.Property(history => history.ChangedAt).IsRequired().HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.Property(history => history.CorrelationId).IsRequired().HasMaxLength(100);
        builder.Property(history => history.Reason).HasMaxLength(1000);

        builder.HasIndex(history => history.FiscalPeriodId)
            .HasDatabaseName("IX_FiscalPeriodStatusHistory_FiscalPeriodId");
    }
}
