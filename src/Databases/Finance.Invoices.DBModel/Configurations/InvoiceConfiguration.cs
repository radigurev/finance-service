using Finance.Invoices.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Invoices.DBModel.Configurations;

/// <summary>
/// EF Core Fluent-API configuration for the <see cref="Invoice"/> aggregate root (SDD-INV-001 §2.3, §2.10).
/// Maps to <c>finance_invoices.Invoices</c> with a sequential-GUID PK, a <c>rowversion</c> concurrency
/// token, enum-as-string discriminator/status columns, <c>DECIMAL(18,2)</c> totals, <c>DATETIMEOFFSET</c>
/// timestamps, and the composed line / status-history collections.
/// </summary>
public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    private const string AmountColumnType = "decimal(18,2)";

    /// <summary>Configures the table, columns, indexes, and relationships for <see cref="Invoice"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Invoices", schema: "finance_invoices");

        builder.HasKey(invoice => invoice.Id).HasName("PK_Invoices");

        builder.Property(invoice => invoice.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

        builder.Property(invoice => invoice.DocumentNumber).HasMaxLength(40);

        builder.Property(invoice => invoice.DocumentType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(invoice => invoice.Direction)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(2);

        builder.Property(invoice => invoice.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(invoice => invoice.CounterpartyId).IsRequired();
        builder.Property(invoice => invoice.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(invoice => invoice.BaseCurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(invoice => invoice.IssueDate).IsRequired();
        builder.Property(invoice => invoice.DueDate).IsRequired();
        builder.Property(invoice => invoice.NetTotal).IsRequired().HasColumnType(AmountColumnType);
        builder.Property(invoice => invoice.TaxTotal).IsRequired().HasColumnType(AmountColumnType);
        builder.Property(invoice => invoice.GrossTotal).IsRequired().HasColumnType(AmountColumnType);
        builder.Property(invoice => invoice.CorrectsInvoiceId);
        builder.Property(invoice => invoice.JournalEntryId);
        builder.Property(invoice => invoice.SourceDocumentId);
        builder.Property(invoice => invoice.SourceDocumentType).HasMaxLength(40);
        builder.Property(invoice => invoice.CorrelationId).IsRequired().HasMaxLength(100);
        builder.Property(invoice => invoice.CreatedAt).IsRequired().HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.Property(invoice => invoice.CreatedBy).IsRequired();
        builder.Property(invoice => invoice.ConfirmedAt);
        builder.Property(invoice => invoice.ConfirmedBy);
        builder.Property(invoice => invoice.PostedAt);
        builder.Property(invoice => invoice.RowVersion).IsRowVersion();

        builder.HasIndex(invoice => invoice.DocumentNumber)
            .IsUnique()
            .HasFilter("[DocumentNumber] IS NOT NULL")
            .HasDatabaseName("IX_Invoices_DocumentNumber");
        builder.HasIndex(invoice => invoice.Status).HasDatabaseName("IX_Invoices_Status");
        builder.HasIndex(invoice => invoice.DocumentType).HasDatabaseName("IX_Invoices_DocumentType");
        builder.HasIndex(invoice => invoice.CounterpartyId).HasDatabaseName("IX_Invoices_CounterpartyId");
        builder.HasIndex(invoice => invoice.IssueDate).HasDatabaseName("IX_Invoices_IssueDate");
        builder.HasIndex(invoice => invoice.CorrectsInvoiceId).HasDatabaseName("IX_Invoices_CorrectsInvoiceId");

        builder.HasIndex(invoice => new { invoice.SourceDocumentType, invoice.SourceDocumentId })
            .IsUnique()
            .HasFilter("[SourceDocumentType] IS NOT NULL AND [SourceDocumentId] IS NOT NULL")
            .HasDatabaseName("IX_Invoices_SourceDocument");

        builder.HasMany(invoice => invoice.Lines)
            .WithOne(line => line.Invoice)
            .HasForeignKey(line => line.InvoiceId)
            .HasConstraintName("FK_InvoiceLines_Invoices")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(invoice => invoice.StatusHistory)
            .WithOne(history => history.Invoice)
            .HasForeignKey(history => history.InvoiceId)
            .HasConstraintName("FK_InvoiceStatusHistory_Invoices")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(invoice => invoice.Lines).AutoInclude();
    }
}
