using Finance.Payments.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Payments.DBModel.Configurations;

/// <summary>
/// EF Core Fluent-API configuration for the append-only <see cref="PaymentStatusHistory"/> entity
/// (SDD-PAY-001 §2.16; SDD-INFRA-008 §2.4). Statuses are stored as strings; the collection is deliberately
/// NOT <c>AutoInclude()</c>d so a list query never drags the history along.
/// </summary>
public sealed class PaymentStatusHistoryConfiguration : IEntityTypeConfiguration<PaymentStatusHistory>
{
    /// <summary>Configures the table, columns, and indexes for <see cref="PaymentStatusHistory"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<PaymentStatusHistory> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PaymentStatusHistory", schema: "payments");

        builder.HasKey(history => history.Id).HasName("PK_PaymentStatusHistory");

        builder.Property(history => history.PaymentId).IsRequired();
        builder.Property(history => history.FromStatus).HasMaxLength(20);
        builder.Property(history => history.ToStatus).IsRequired().HasMaxLength(20);
        builder.Property(history => history.ChangedBy).IsRequired();
        builder.Property(history => history.ChangedAt).IsRequired().HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.Property(history => history.CorrelationId).IsRequired().HasMaxLength(100);
        builder.Property(history => history.Reason).HasMaxLength(1000);

        builder.HasIndex(history => history.PaymentId)
            .HasDatabaseName("IX_PaymentStatusHistory_PaymentId");
    }
}
