using Finance.Payments.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Payments.DBModel.Configurations;

/// <summary>
/// EF Core Fluent-API configuration for the <see cref="InvoiceOpenItem"/> local read projection
/// (SDD-PAY-002 §2.2, §2.12). Maps to <c>payments.InvoiceOpenItems</c> with the MIRRORED
/// <see cref="InvoiceOpenItem.InvoiceId"/> as the primary key — <c>ValueGeneratedNever()</c>, no surrogate
/// identity and deliberately NO <c>NEWSEQUENTIALID()</c> default, because the value always arrives on the
/// source event.
/// <para>Amounts are <c>DECIMAL(18,2)</c>, the booking rate is <c>DECIMAL(18,6)</c>, dates are
/// <c>DATETIMEOFFSET</c>, and the <c>rowversion</c> token is REQUIRED — it serializes two payments allocating
/// against the same invoice concurrently. The computed outstanding amount is ignored by EF: it is derivable,
/// and a stored copy would be a second source of truth.</para>
/// <para>This configuration carries the SINGLE definition of the projection's index set, and it is exactly
/// three indexes: one on the counterparty, one on the due date, and the composite
/// <c>{ Direction, InvoiceStatus, CounterpartyId, DueDate }</c> that covers the SDD-PAY-003 aging predicate
/// (filter on direction plus eligible status, group by counterparty, bucket by due date) in one seek.
/// SDD-PAY-003 adds none of its own. No foreign key to <c>finance_invoices</c> exists or may be added.</para>
/// </summary>
public sealed class InvoiceOpenItemConfiguration : IEntityTypeConfiguration<InvoiceOpenItem>
{
    private const string AmountColumnType = "decimal(18,2)";
    private const string RateColumnType = "decimal(18,6)";
    private const string Schema = "payments";

    /// <summary>Configures the table, columns, and index set for <see cref="InvoiceOpenItem"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<InvoiceOpenItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("InvoiceOpenItems", schema: Schema);

        builder.HasKey(item => item.InvoiceId).HasName("PK_InvoiceOpenItems");
        builder.Property(item => item.InvoiceId).ValueGeneratedNever();

        ConfigureDescriptors(builder);
        ConfigureAmounts(builder);
        ConfigureStamps(builder);
        ConfigureIndexes(builder);

        builder.Ignore(item => item.Outstanding);
    }

    private static void ConfigureDescriptors(EntityTypeBuilder<InvoiceOpenItem> builder)
    {
        builder.Property(item => item.DocumentNumber).IsRequired().HasMaxLength(40);
        builder.Property(item => item.DocumentType).IsRequired().HasMaxLength(30);
        builder.Property(item => item.Direction).IsRequired().HasMaxLength(2);
        builder.Property(item => item.CounterpartyId).IsRequired();
        builder.Property(item => item.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(item => item.BaseCurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(item => item.InvoiceStatus).IsRequired().HasMaxLength(20);
    }

    private static void ConfigureAmounts(EntityTypeBuilder<InvoiceOpenItem> builder)
    {
        builder.Property(item => item.GrossTotal).IsRequired().HasColumnType(AmountColumnType);

        builder.Property(item => item.BookingExchangeRate)
            .IsRequired()
            .HasColumnType(RateColumnType);

        builder.Property(item => item.SettledAmount)
            .IsRequired()
            .HasColumnType(AmountColumnType)
            .HasDefaultValue(0m);
    }

    private static void ConfigureStamps(EntityTypeBuilder<InvoiceOpenItem> builder)
    {
        builder.Property(item => item.IssueDate).IsRequired();
        builder.Property(item => item.DueDate).IsRequired();

        builder.Property(item => item.LastAppliedAt)
            .IsRequired()
            .HasDefaultValueSql("SYSDATETIMEOFFSET()");

        builder.Property(item => item.RowVersion).IsRowVersion();
    }

    private static void ConfigureIndexes(EntityTypeBuilder<InvoiceOpenItem> builder)
    {
        builder.HasIndex(item => item.CounterpartyId)
            .HasDatabaseName("IX_InvoiceOpenItems_CounterpartyId");

        builder.HasIndex(item => item.DueDate)
            .HasDatabaseName("IX_InvoiceOpenItems_DueDate");

        builder.HasIndex(item => new
            {
                item.Direction,
                item.InvoiceStatus,
                item.CounterpartyId,
                item.DueDate
            })
            .HasDatabaseName("IX_InvoiceOpenItems_Direction_InvoiceStatus_CounterpartyId_DueDate");
    }
}
