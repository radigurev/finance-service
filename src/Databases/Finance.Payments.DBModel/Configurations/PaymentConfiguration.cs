using Finance.Payments.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Payments.DBModel.Configurations;

/// <summary>
/// EF Core Fluent-API configuration for the <see cref="Payment"/> aggregate root (SDD-PAY-001 §2.16). Maps to
/// <c>payments.Payments</c> with a sequential-GUID PK, a <c>rowversion</c> concurrency token, enum-as-string
/// discriminator/status columns, <c>DECIMAL(18,2)</c> amounts and a <c>DECIMAL(18,6)</c> rate,
/// <c>DATETIMEOFFSET</c> timestamps, the two UNIQUE FILTERED indexes that make a duplicate document number
/// and a double journal-entry link impossible, and the append-only status-history collection. Every property
/// is configured explicitly — nothing is left to convention.
/// </summary>
public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    private const string AmountColumnType = "decimal(18,2)";
    private const string RateColumnType = "decimal(18,6)";
    private const string Schema = "payments";

    /// <summary>Configures the table, columns, indexes, and relationships for <see cref="Payment"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Payments", schema: Schema);

        builder.HasKey(payment => payment.Id).HasName("PK_Payments");

        builder.Property(payment => payment.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

        ConfigureDiscriminators(builder);
        ConfigureAmounts(builder);
        ConfigureStamps(builder);
        ConfigureIndexes(builder);

        builder.Ignore(payment => payment.UnallocatedAmount);

        builder.HasMany(payment => payment.StatusHistory)
            .WithOne(history => history.Payment)
            .HasForeignKey(history => history.PaymentId)
            .HasConstraintName("FK_PaymentStatusHistory_Payments")
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureDiscriminators(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(payment => payment.DocumentNumber).HasMaxLength(40);

        builder.Property(payment => payment.DocumentType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(payment => payment.Direction)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(2);

        builder.Property(payment => payment.Method)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(payment => payment.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(payment => payment.CounterpartyId).IsRequired();
        builder.Property(payment => payment.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(payment => payment.BaseCurrencyCode).IsRequired().HasMaxLength(3);
    }

    private static void ConfigureAmounts(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(payment => payment.Amount).IsRequired().HasColumnType(AmountColumnType);
        builder.Property(payment => payment.ExchangeRate).IsRequired().HasColumnType(RateColumnType);
        builder.Property(payment => payment.BaseAmount).IsRequired().HasColumnType(AmountColumnType);
        builder.Property(payment => payment.AllocatedAmount)
            .IsRequired()
            .HasColumnType(AmountColumnType)
            .HasDefaultValue(0m);

        builder.Property(payment => payment.SettlementAccountId).IsRequired();
        builder.Property(payment => payment.PaymentDate).IsRequired();
        builder.Property(payment => payment.BankReference).HasMaxLength(64);
    }

    private static void ConfigureStamps(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(payment => payment.JournalEntryId);
        builder.Property(payment => payment.CancellationReason).HasMaxLength(1000);
        builder.Property(payment => payment.CorrelationId).IsRequired().HasMaxLength(100);
        builder.Property(payment => payment.CreatedAt).IsRequired().HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.Property(payment => payment.CreatedBy).IsRequired();
        builder.Property(payment => payment.ConfirmedAt);
        builder.Property(payment => payment.ConfirmedBy);
        builder.Property(payment => payment.PostedAt);
        builder.Property(payment => payment.ReversedAt);
        builder.Property(payment => payment.RowVersion).IsRowVersion();
    }

    private static void ConfigureIndexes(EntityTypeBuilder<Payment> builder)
    {
        builder.HasIndex(payment => payment.DocumentNumber)
            .IsUnique()
            .HasFilter("[DocumentNumber] IS NOT NULL")
            .HasDatabaseName("IX_Payments_DocumentNumber");

        builder.HasIndex(payment => payment.JournalEntryId)
            .IsUnique()
            .HasFilter("[JournalEntryId] IS NOT NULL")
            .HasDatabaseName("IX_Payments_JournalEntryId");

        builder.HasIndex(payment => payment.Status).HasDatabaseName("IX_Payments_Status");
        builder.HasIndex(payment => payment.DocumentType).HasDatabaseName("IX_Payments_DocumentType");
        builder.HasIndex(payment => payment.Direction).HasDatabaseName("IX_Payments_Direction");
        builder.HasIndex(payment => payment.CounterpartyId).HasDatabaseName("IX_Payments_CounterpartyId");
        builder.HasIndex(payment => payment.PaymentDate).HasDatabaseName("IX_Payments_PaymentDate");
        builder.HasIndex(payment => payment.SettlementAccountId)
            .HasDatabaseName("IX_Payments_SettlementAccountId");
    }
}
