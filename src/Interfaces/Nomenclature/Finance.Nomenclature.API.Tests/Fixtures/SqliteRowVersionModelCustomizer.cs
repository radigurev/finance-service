using Finance.Nomenclature.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Finance.Nomenclature.API.Tests.Fixtures;

/// <summary>
/// Post-processes the built model so the <see cref="Currency.RowVersion"/> concurrency token is
/// application-generated instead of store-generated, and so SQLite can order and compare
/// <see cref="ExchangeRate.RateDate"/>. SQL Server auto-fills <c>rowversion</c> and natively compares
/// <c>DATETIMEOFFSET</c>, but SQLite does neither: the production <c>IsRowVersion()</c> mapping is
/// rewritten to a never-generated concurrency token (supplied by <see cref="SqliteRowVersionInterceptor"/>),
/// and the rate date is stored via a UTC-ticks converter so range / latest-on-or-before queries translate.
/// This keeps the optimistic-concurrency (SDD-NOM-001 §2.1) and exchange-rate read (SDD-NOM-001 §2.2)
/// behavior observable in offline SQLite unit tests without touching production code.
/// </summary>
public sealed class SqliteRowVersionModelCustomizer : RelationalModelCustomizer
{
    private static readonly ValueConverter<DateTimeOffset, long> RateDateConverter =
        new(value => value.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

    /// <summary>Creates the customizer with the supplied dependencies.</summary>
    /// <param name="dependencies">The model-customizer dependencies supplied by EF Core.</param>
    public SqliteRowVersionModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        IMutableProperty? rowVersion = modelBuilder.Model
            .FindEntityType(typeof(Currency))?
            .FindProperty(nameof(Currency.RowVersion));

        if (rowVersion is not null)
        {
            rowVersion.ValueGenerated = ValueGenerated.Never;
            rowVersion.IsConcurrencyToken = true;
        }

        IMutableProperty? rateDate = modelBuilder.Model
            .FindEntityType(typeof(ExchangeRate))?
            .FindProperty(nameof(ExchangeRate.RateDate));

        rateDate?.SetValueConverter(RateDateConverter);
    }
}
