using Finance.Periods.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Finance.Periods.API.Tests.Fixtures;

/// <summary>
/// Post-processes the built Periods model so SQL-Server-only behaviour works on SQLite for the offline unit
/// tests (SDD-FIN-004 §6). SQLite cannot auto-fill a <c>rowversion</c> token, evaluate the
/// <c>SYSDATETIMEOFFSET()</c> column defaults, or compare/<c>ORDER BY</c> a <c>DateTimeOffset</c>. This
/// customizer rewrites <see cref="FiscalPeriod.RowVersion"/> to a never-generated concurrency token that
/// <see cref="SqlitePeriodsRowVersionInterceptor"/> stamps on each write, strips the
/// <c>SYSDATETIMEOFFSET()</c> defaults, and applies a binary <c>DateTimeOffset</c> converter so the
/// by-date range lookup and the default ordering (SDD-FIN-004 §2.6, §2.11) are sortable — all leaving
/// production code untouched.
/// </summary>
public sealed class SqlitePeriodsModelCustomizer : RelationalModelCustomizer
{
    private static readonly DateTimeOffsetToBinaryConverter DateTimeOffsetConverter = new();

    /// <summary>Creates the customizer with the supplied dependencies.</summary>
    /// <param name="dependencies">The model-customizer dependencies supplied by EF Core.</param>
    public SqlitePeriodsModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        ApplyDateTimeOffsetConverter(modelBuilder);

        IMutableEntityType? periodType = modelBuilder.Model.FindEntityType(typeof(FiscalPeriod));
        if (periodType is null)
        {
            return;
        }

        IMutableProperty? rowVersion = periodType.FindProperty(nameof(FiscalPeriod.RowVersion));
        if (rowVersion is not null)
        {
            rowVersion.ValueGenerated = ValueGenerated.Never;
            rowVersion.IsConcurrencyToken = true;
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
