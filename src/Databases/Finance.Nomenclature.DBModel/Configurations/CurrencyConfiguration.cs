using Finance.Nomenclature.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Nomenclature.DBModel.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="Currency"/> entity (SDD-NOM-001 §2.0). The
/// <c>IsoCode</c> column carries a unique index; the table lives in the <c>nomenclature</c> schema.
/// </summary>
public sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    /// <summary>Configures the table, columns, and indexes for <see cref="Currency"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("Currencies", schema: "nomenclature");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.IsoCode).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(c => c.Name).IsRequired().HasMaxLength(100);
        builder.Property(c => c.Symbol).HasMaxLength(5);
        builder.Property(c => c.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(c => c.CreatedAt).IsRequired().HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.Property(c => c.UpdatedAt);
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasIndex(c => c.IsoCode).IsUnique();
    }
}
