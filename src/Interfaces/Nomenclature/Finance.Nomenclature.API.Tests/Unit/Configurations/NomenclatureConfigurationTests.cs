using Finance.Nomenclature.DBModel;
using Finance.Nomenclature.DBModel.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NUnit.Framework;

namespace Finance.Nomenclature.API.Tests.Unit.Configurations;

/// <summary>
/// Unit tests for the production Currency and ExchangeRate Fluent API mappings (SDD-NOM-001 §2.0). Builds
/// the real <see cref="NomenclatureDbContext"/> model (no test customizations) and inspects metadata for
/// the nomenclature schema, unique indexes, the row-version concurrency token, and the decimal rate type.
/// </summary>
[TestFixture]
[Category("SDD-NOM-001")]
public sealed class NomenclatureConfigurationTests
{
    private SqliteConnection _connection = null!;
    private NomenclatureDbContext _context = null!;

    /// <summary>Builds the real model over an in-memory SQLite connection before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<NomenclatureDbContext> options = new DbContextOptionsBuilder<NomenclatureDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new NomenclatureDbContext(options);
    }

    /// <summary>Disposes the context and connection after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    /// <summary>The currency maps to the Currencies table in the nomenclature schema (§2.0).</summary>
    [Test]
    public void CurrencyConfiguration_MapsToCurrenciesTableInNomenclatureSchema()
    {
        // Arrange
        IEntityType entityType = _context.Model.FindEntityType(typeof(Currency))!;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(entityType.GetTableName(), Is.EqualTo("Currencies"));
            Assert.That(entityType.GetSchema(), Is.EqualTo("nomenclature"));
        });
    }

    /// <summary>There is a unique index over IsoCode enforcing currency-code uniqueness (§2.0).</summary>
    [Test]
    public void CurrencyConfiguration_HasUniqueIndexOnIsoCode()
    {
        // Arrange
        IEntityType entityType = _context.Model.FindEntityType(typeof(Currency))!;

        // Act
        IIndex? isoIndex = entityType.GetIndexes().FirstOrDefault(index =>
            index.Properties.Count == 1 && index.Properties[0].Name == nameof(Currency.IsoCode));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(isoIndex, Is.Not.Null);
            Assert.That(isoIndex!.IsUnique, Is.True);
        });
    }

    /// <summary>RowVersion is configured as a store-generated concurrency token (§2.0).</summary>
    [Test]
    public void CurrencyConfiguration_ConfiguresRowVersionConcurrencyToken()
    {
        // Arrange
        IEntityType entityType = _context.Model.FindEntityType(typeof(Currency))!;
        IProperty rowVersion = entityType.FindProperty(nameof(Currency.RowVersion))!;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(rowVersion.IsConcurrencyToken, Is.True);
            Assert.That(rowVersion.ValueGenerated, Is.EqualTo(ValueGenerated.OnAddOrUpdate));
        });
    }

    /// <summary>The exchange rate maps to the ExchangeRates table in the nomenclature schema (§2.0).</summary>
    [Test]
    public void ExchangeRateConfiguration_MapsToExchangeRatesTableInNomenclatureSchema()
    {
        // Arrange
        IEntityType entityType = _context.Model.FindEntityType(typeof(ExchangeRate))!;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(entityType.GetTableName(), Is.EqualTo("ExchangeRates"));
            Assert.That(entityType.GetSchema(), Is.EqualTo("nomenclature"));
        });
    }

    /// <summary>There is a unique index over (CurrencyIsoCode, RateDate) — one rate per currency per date (§2.0).</summary>
    [Test]
    public void ExchangeRateConfiguration_HasUniqueIndexOnCurrencyAndDate()
    {
        // Arrange
        IEntityType entityType = _context.Model.FindEntityType(typeof(ExchangeRate))!;

        // Act
        IIndex? compositeIndex = entityType.GetIndexes().FirstOrDefault(index =>
            index.Properties.Count == 2
            && index.Properties[0].Name == nameof(ExchangeRate.CurrencyIsoCode)
            && index.Properties[1].Name == nameof(ExchangeRate.RateDate));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(compositeIndex, Is.Not.Null);
            Assert.That(compositeIndex!.IsUnique, Is.True);
        });
    }

    /// <summary>The rate column uses decimal(18,6) precision (§2.0).</summary>
    [Test]
    public void ExchangeRateConfiguration_ConfiguresDecimalRatePrecision()
    {
        // Arrange
        IEntityType entityType = _context.Model.FindEntityType(typeof(ExchangeRate))!;
        IProperty rate = entityType.FindProperty(nameof(ExchangeRate.Rate))!;

        // Assert
        Assert.That(rate.GetColumnType(), Is.EqualTo("decimal(18,6)"));
    }
}
