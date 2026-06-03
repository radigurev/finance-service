using Finance.Journal.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Journal.DBModel.Configurations;

/// <summary>
/// EF Core Fluent-API configuration for the <see cref="JournalEntryLine"/> entity (SDD-FIN-001 §2.2).
/// Monetary amounts are <c>DECIMAL(18,2)</c> and the exchange rate is <c>DECIMAL(18,6)</c> per
/// SDD-FIN-005 / SDD-INFRA-001 — never <c>float</c>/<c>double</c>.
/// </summary>
public sealed class JournalEntryLineConfiguration : IEntityTypeConfiguration<JournalEntryLine>
{
    private const string AmountColumnType = "decimal(18,2)";
    private const string RateColumnType = "decimal(18,6)";

    /// <summary>Configures the table, columns, and indexes for <see cref="JournalEntryLine"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<JournalEntryLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("JournalEntryLines", schema: "journal");

        builder.HasKey(line => line.Id).HasName("PK_JournalEntryLines");

        builder.Property(line => line.JournalEntryId).IsRequired();
        builder.Property(line => line.AccountId).IsRequired();
        builder.Property(line => line.DebitAmount).IsRequired().HasColumnType(AmountColumnType);
        builder.Property(line => line.CreditAmount).IsRequired().HasColumnType(AmountColumnType);
        builder.Property(line => line.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(line => line.ExchangeRate).IsRequired().HasColumnType(RateColumnType);
        builder.Property(line => line.BaseDebitAmount).IsRequired().HasColumnType(AmountColumnType);
        builder.Property(line => line.BaseCreditAmount).IsRequired().HasColumnType(AmountColumnType);
        builder.Property(line => line.LineNumber).IsRequired();
        builder.Property(line => line.Description).HasMaxLength(500);

        builder.HasIndex(line => line.JournalEntryId).HasDatabaseName("IX_JournalEntryLines_JournalEntryId");
        builder.HasIndex(line => line.AccountId).HasDatabaseName("IX_JournalEntryLines_AccountId");
    }
}
