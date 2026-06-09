using Finance.Invoices.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Invoices.DBModel.Configurations;

/// <summary>
/// EF Core Fluent-API configuration for the append-only <see cref="InvoiceStatusHistory"/> entity
/// (SDD-INV-001 §2.4-§2.7; SDD-INFRA-008 §2.4).
/// </summary>
public sealed class InvoiceStatusHistoryConfiguration : IEntityTypeConfiguration<InvoiceStatusHistory>
{
    /// <summary>Configures the table, columns, and indexes for <see cref="InvoiceStatusHistory"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<InvoiceStatusHistory> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("InvoiceStatusHistory", schema: "finance_invoices");

        builder.HasKey(history => history.Id).HasName("PK_InvoiceStatusHistory");

        builder.Property(history => history.InvoiceId).IsRequired();
        builder.Property(history => history.FromStatus).HasMaxLength(20);
        builder.Property(history => history.ToStatus).IsRequired().HasMaxLength(20);
        builder.Property(history => history.ChangedBy).IsRequired();
        builder.Property(history => history.ChangedAt).IsRequired().HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.Property(history => history.CorrelationId).IsRequired().HasMaxLength(100);
        builder.Property(history => history.Reason).HasMaxLength(1000);

        builder.HasIndex(history => history.InvoiceId)
            .HasDatabaseName("IX_InvoiceStatusHistory_InvoiceId");
    }
}
