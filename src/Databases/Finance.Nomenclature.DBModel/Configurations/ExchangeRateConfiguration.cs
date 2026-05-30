using Finance.Nomenclature.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Nomenclature.DBModel.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="ExchangeRate"/> entity (SDD-NOM-001 §2.0). The
/// <c>(CurrencyIsoCode, RateDate)</c> pair carries a unique index so at most one rate exists per
/// currency per date; the rate uses <c>DECIMAL(18,6)</c> precision.
/// </summary>
public sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    /// <summary>Configures the table, columns, and indexes for <see cref="ExchangeRate"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("ExchangeRates", schema: "nomenclature");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.CurrencyIsoCode).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(r => r.Rate).IsRequired().HasColumnType("decimal(18,6)");
        builder.Property(r => r.RateDate).IsRequired();

        builder.HasIndex(r => new { r.CurrencyIsoCode, r.RateDate }).IsUnique();
    }
}
