using Finance.Payments.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Finance.Payments.API.Tests.Fixtures;

/// <summary>
/// Post-processes the built Payments model so SQL-Server-only behaviour works on SQLite for the offline unit
/// tests (SDD-PAY-001 §6, SDD-PAY-002 §6, SDD-PAY-003 §6). SQLite cannot evaluate the production
/// <c>NEWSEQUENTIALID()</c> PK default, auto-fill a <c>rowversion</c> token, compare or <c>ORDER BY</c> a
/// <c>DateTimeOffset</c>, or order a <c>decimal</c> stored as text. This customizer drops the
/// <see cref="Payment.Id"/> generation (the interceptor supplies the GUID), rewrites every
/// <c>RowVersion</c> to a never-generated concurrency token that
/// <see cref="SqlitePaymentsRowVersionInterceptor"/> stamps on each write, applies a binary
/// <c>DateTimeOffset</c> converter so the aging <c>DueDate</c>/<c>IssueDate</c> predicates and the payment
/// <c>PaymentDate</c> ordering are sortable, and applies scaled-<c>long</c> decimal converters so monetary
/// comparisons are exact — all leaving production code untouched.
/// <para>Mirrors <c>SqliteInvoicesModelCustomizer</c> in the shipped Invoices test project.</para>
/// </summary>
public sealed class SqlitePaymentsModelCustomizer : RelationalModelCustomizer
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
    public SqlitePaymentsModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        ApplyDateTimeOffsetConverter(modelBuilder);
        ApplyDecimalConverters(modelBuilder);
        RewriteStoreGeneratedColumns(modelBuilder);
    }

    private static void RewriteStoreGeneratedColumns(ModelBuilder modelBuilder)
    {
        RewritePaymentColumns(modelBuilder);
        RewriteRowVersion(modelBuilder, typeof(PaymentAllocation), nameof(PaymentAllocation.RowVersion));
        RewriteRowVersion(modelBuilder, typeof(InvoiceOpenItem), nameof(InvoiceOpenItem.RowVersion));
    }

    private static void RewritePaymentColumns(ModelBuilder modelBuilder)
    {
        IMutableEntityType? paymentType = modelBuilder.Model.FindEntityType(typeof(Payment));
        if (paymentType is null)
        {
            return;
        }

        IMutableProperty? id = paymentType.FindProperty(nameof(Payment.Id));
        if (id is not null)
        {
            id.ValueGenerated = ValueGenerated.Never;
            id.SetDefaultValueSql(null);
        }

        RewriteRowVersion(modelBuilder, typeof(Payment), nameof(Payment.RowVersion));
    }

    private static void RewriteRowVersion(ModelBuilder modelBuilder, Type clrType, string propertyName)
    {
        IMutableEntityType? entityType = modelBuilder.Model.FindEntityType(clrType);
        IMutableProperty? rowVersion = entityType?.FindProperty(propertyName);
        if (rowVersion is null)
        {
            return;
        }

        rowVersion.ValueGenerated = ValueGenerated.Never;
        rowVersion.IsConcurrencyToken = true;
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
                property.SetDefaultValue(null);
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
