using Finance.Invoices.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Finance.Invoices.API.Tests.Fixtures;

/// <summary>
/// Post-processes the built Invoices model so SQL-Server-only behaviour works on SQLite for the offline unit
/// tests (SDD-INV-001 §6, SDD-INT-WH-001 §6). SQLite cannot evaluate the production <c>NEWSEQUENTIALID()</c>
/// PK default, auto-fill a <c>rowversion</c> token, or <c>ORDER BY</c> a <c>DateTimeOffset</c>. This
/// customizer drops the <see cref="Invoice.Id"/> generation (the interceptor supplies the GUID), rewrites
/// <see cref="Invoice.RowVersion"/> to a never-generated concurrency token that
/// <see cref="SqliteInvoicesRowVersionInterceptor"/> stamps on each write, and applies a binary
/// <c>DateTimeOffset</c> converter so the default <c>IssueDate</c> ordering (SDD-INV-001 §2.10) is sortable —
/// all leaving production code untouched.
/// </summary>
public sealed class SqliteInvoicesModelCustomizer : RelationalModelCustomizer
{
    private const decimal AmountScale = 100m;
    private const decimal RateScale = 1000000m;

    private static readonly DateTimeOffsetToBinaryConverter DateTimeOffsetConverter = new();

    private static readonly ValueConverter<decimal, long> AmountConverter =
        new(value => (long)decimal.Round(value * AmountScale, 0, MidpointRounding.AwayFromZero),
            stored => stored / AmountScale);

    private static readonly ValueConverter<decimal, long> RateConverter =
        new(value => (long)decimal.Round(value * RateScale, 0, MidpointRounding.AwayFromZero),
            stored => stored / RateScale);

    /// <summary>Creates the customizer with the supplied dependencies.</summary>
    /// <param name="dependencies">The model-customizer dependencies supplied by EF Core.</param>
    public SqliteInvoicesModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        ApplyDateTimeOffsetConverter(modelBuilder);
        ApplyDecimalConverters(modelBuilder);
        RewriteInvoiceStoreGeneratedColumns(modelBuilder);
    }

    private static void RewriteInvoiceStoreGeneratedColumns(ModelBuilder modelBuilder)
    {
        IMutableEntityType? invoiceType = modelBuilder.Model.FindEntityType(typeof(Invoice));
        if (invoiceType is null)
        {
            return;
        }

        IMutableProperty? id = invoiceType.FindProperty(nameof(Invoice.Id));
        if (id is not null)
        {
            id.ValueGenerated = ValueGenerated.Never;
            id.SetDefaultValueSql(null);
        }

        IMutableProperty? createdAt = invoiceType.FindProperty(nameof(Invoice.CreatedAt));
        createdAt?.SetDefaultValueSql(null);

        IMutableProperty? rowVersion = invoiceType.FindProperty(nameof(Invoice.RowVersion));
        if (rowVersion is not null)
        {
            rowVersion.ValueGenerated = ValueGenerated.Never;
            rowVersion.IsConcurrencyToken = true;
        }

        IMutableEntityType? historyType = modelBuilder.Model.FindEntityType(typeof(InvoiceStatusHistory));
        IMutableProperty? historyChangedAt = historyType?.FindProperty(nameof(InvoiceStatusHistory.ChangedAt));
        historyChangedAt?.SetDefaultValueSql(null);
    }

    private static void ApplyDecimalConverters(ModelBuilder modelBuilder)
    {
        foreach (IMutableEntityType entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (IMutableProperty property in entity.GetProperties())
            {
                if (property.ClrType != typeof(decimal) && property.ClrType != typeof(decimal?))
                {
                    continue;
                }

                string? columnType = property.GetColumnType();
                bool isRate = columnType is not null && columnType.Contains(",6", StringComparison.OrdinalIgnoreCase);
                property.SetValueConverter(isRate ? RateConverter : AmountConverter);
                property.SetColumnType(null);
            }
        }
    }

    private static void ApplyDateTimeOffsetConverter(ModelBuilder modelBuilder)
    {
        foreach (IMutableEntityType entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (IMutableProperty property in entity.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(DateTimeOffsetConverter);
                    property.SetDefaultValueSql(null);
                }
            }
        }
    }
}
