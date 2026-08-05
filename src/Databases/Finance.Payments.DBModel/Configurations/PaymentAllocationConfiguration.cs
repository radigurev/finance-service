using Finance.Payments.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Payments.DBModel.Configurations;

/// <summary>
/// EF Core Fluent-API configuration for the <see cref="PaymentAllocation"/> match row (SDD-PAY-002 §2.1,
/// §2.12). Maps to <c>payments.PaymentAllocations</c> with an <c>INT IDENTITY</c> PK, a cascading foreign key
/// to the owning payment, <c>DECIMAL(18,2)</c> amounts, a <c>DATETIMEOFFSET</c> stamp defaulting to
/// <c>SYSDATETIMEOFFSET()</c>, and a <c>rowversion</c> concurrency token.
/// <para><see cref="PaymentAllocation.InvoiceId"/> is a CROSS-SERVICE reference and is deliberately configured
/// with NO foreign key — the invoice lives in another service's database (a cross-database join is
/// forbidden). Its uniqueness partner, the UNIQUE index <c>IX_PaymentAllocations_PaymentInvoice</c> over
/// <c>(PaymentId, InvoiceId)</c>, is the database-level backstop for
/// <c>PAYMENT_ALLOCATION_DUPLICATE</c>; both columns are <c>NOT NULL</c>, so it is unfiltered.</para>
/// </summary>
public sealed class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    private const string AmountColumnType = "decimal(18,2)";
    private const string Schema = "payments";

    /// <summary>Configures the table, columns, indexes, and relationship for <see cref="PaymentAllocation"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PaymentAllocations", schema: Schema);

        builder.HasKey(allocation => allocation.Id).HasName("PK_PaymentAllocations");
        builder.Property(allocation => allocation.Id).ValueGeneratedOnAdd();

        ConfigureColumns(builder);
        ConfigureIndexes(builder);

        builder.HasOne(allocation => allocation.Payment)
            .WithMany(payment => payment.Allocations)
            .HasForeignKey(allocation => allocation.PaymentId)
            .HasConstraintName("FK_PaymentAllocations_Payments")
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureColumns(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.Property(allocation => allocation.PaymentId).IsRequired();
        builder.Property(allocation => allocation.InvoiceId).IsRequired();

        builder.Property(allocation => allocation.AllocatedAmount)
            .IsRequired()
            .HasColumnType(AmountColumnType);

        builder.Property(allocation => allocation.BaseAllocatedAmount)
            .IsRequired()
            .HasColumnType(AmountColumnType);

        builder.Property(allocation => allocation.RealizedFxDifference)
            .IsRequired()
            .HasColumnType(AmountColumnType)
            .HasDefaultValue(0m);

        builder.Property(allocation => allocation.AllocatedAt)
            .IsRequired()
            .HasDefaultValueSql("SYSDATETIMEOFFSET()");

        builder.Property(allocation => allocation.AllocatedBy).IsRequired();
        builder.Property(allocation => allocation.CorrelationId).IsRequired().HasMaxLength(100);
        builder.Property(allocation => allocation.RowVersion).IsRowVersion();
    }

    private static void ConfigureIndexes(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.HasIndex(allocation => allocation.PaymentId)
            .HasDatabaseName("IX_PaymentAllocations_PaymentId");

        builder.HasIndex(allocation => allocation.InvoiceId)
            .HasDatabaseName("IX_PaymentAllocations_InvoiceId");

        builder.HasIndex(allocation => new { allocation.PaymentId, allocation.InvoiceId })
            .IsUnique()
            .HasDatabaseName("IX_PaymentAllocations_PaymentInvoice");
    }
}
