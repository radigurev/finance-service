using Finance.Periods.API.Tests.Fixtures;
using Finance.Periods.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NUnit.Framework;

namespace Finance.Periods.API.Tests.Unit.Configurations;

/// <summary>
/// Unit tests for the <see cref="Finance.Periods.DBModel.Configurations.FiscalPeriodConfiguration"/> Fluent
/// API mapping (SDD-FIN-004 §3, §6.3). Verifies the unique natural-key index, the RowVersion concurrency
/// token, and the enum-as-string status column over the real built model.
/// </summary>
[TestFixture]
[Category("SDD-FIN-004")]
public sealed class FiscalPeriodConfigurationTests
{
    private SqlitePeriodsDbContextScope _scope = null!;

    /// <summary>Builds a fresh SQLite-backed context before each test so the model is materialized.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqlitePeriodsDbContextFactory.Create();
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    /// <summary>The (FiscalYear, PeriodNumber) natural key is a unique index (§3, §6.3).</summary>
    [Test]
    public void FiscalPeriodConfiguration_HasUniqueIndexOnFiscalYearAndPeriodNumber()
    {
        // Arrange
        IEntityType entityType = _scope.Context.Model.FindEntityType(typeof(FiscalPeriod))!;

        // Act
        IIndex? index = entityType.GetIndexes().FirstOrDefault(candidate =>
            candidate.Properties.Count == 2
            && candidate.Properties[0].Name == nameof(FiscalPeriod.FiscalYear)
            && candidate.Properties[1].Name == nameof(FiscalPeriod.PeriodNumber));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(index, Is.Not.Null);
            Assert.That(index!.IsUnique, Is.True);
        });
    }

    /// <summary>RowVersion is configured as a concurrency token (§2.12, §6.3).</summary>
    [Test]
    public void FiscalPeriodConfiguration_ConfiguresRowVersionConcurrencyToken()
    {
        // Arrange
        IEntityType entityType = _scope.Context.Model.FindEntityType(typeof(FiscalPeriod))!;

        // Act
        IProperty rowVersion = entityType.FindProperty(nameof(FiscalPeriod.RowVersion))!;

        // Assert
        Assert.That(rowVersion.IsConcurrencyToken, Is.True);
    }

    /// <summary>Status is persisted as its string name (enum-as-string conversion) (§3, §6.3).</summary>
    [Test]
    public void FiscalPeriodConfiguration_PersistsStatusAsString()
    {
        // Arrange
        IEntityType entityType = _scope.Context.Model.FindEntityType(typeof(FiscalPeriod))!;

        // Act
        IProperty status = entityType.FindProperty(nameof(FiscalPeriod.Status))!;

        // Assert
        Assert.That(status.GetProviderClrType() ?? status.ClrType, Is.EqualTo(typeof(string)));
    }
}
