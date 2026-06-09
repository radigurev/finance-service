using Finance.Invoices.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Invoices.DBModel.Configurations;

/// <summary>
/// EF Core Fluent-API configuration for the <see cref="InvoiceLine"/> entity (SDD-INV-001 §2.8). Monetary
/// amounts are <c>DECIMAL(18,2)</c> and the tax rate is <c>DECIMAL(18,6)</c> per SDD-FIN-005 — never
/// <c>float</c>/<c>double</c>.
/// </summary>
public sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    private const string AmountColumnType = "decimal(18,2)";
    private const string RateColumnType = "decimal(18,6)";

    /// <summary>Configures the table, columns, and indexes for <see cref="InvoiceLine"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("InvoiceLines", schema: "finance_invoices");

        builder.HasKey(line => line.Id).HasName("PK_InvoiceLines");

        builder.Property(line => line.InvoiceId).IsRequired();
        builder.Property(line => line.LineNumber).IsRequired();
        builder.Property(line => line.Description).IsRequired().HasMaxLength(500);
        builder.Property(line => line.Quantity).IsRequired().HasColumnType(AmountColumnType);
        builder.Property(line => line.UnitPrice).IsRequired().HasColumnType(AmountColumnType);
        builder.Property(line => line.TaxRate).IsRequired().HasColumnType(RateColumnType);
        builder.Property(line => line.LineNet).IsRequired().HasColumnType(AmountColumnType);
        builder.Property(line => line.LineTax).IsRequired().HasColumnType(AmountColumnType);
        builder.Property(line => line.LineGross).IsRequired().HasColumnType(AmountColumnType);

        builder.HasIndex(line => line.InvoiceId).HasDatabaseName("IX_InvoiceLines_InvoiceId");
    }
}
