using Finance.Common.ErrorCodes;
using Finance.Journal.API.ErrorMapping;
using Finance.Journal.DBModel;
using Finance.Journal.DBModel.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit.Configurations;

/// <summary>
/// Unit tests for the Journal EF Core Fluent-API mapping and the Journal error-code → HTTP-status map
/// (SDD-FIN-001 §6.4). The PK is a sequential GUID, the <c>RowVersion</c> is a concurrency token, monetary
/// line amounts are <c>decimal(18,2)</c>, and the exchange rate is <c>decimal(18,6)</c>. The mapping is
/// inspected against the built model on a SQL-Server-shaped context so the production column types are
/// asserted directly.
/// </summary>
[TestFixture]
[Category("SDD-FIN-001")]
public sealed class JournalConfigurationTests
{
    private IModel _model = null!;

    /// <summary>Builds the production SQL Server model once for inspection.</summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        DbContextOptions<JournalDbContext> options = new DbContextOptionsBuilder<JournalDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=model-only")
            .Options;

        using JournalDbContext context = new(options);
        _model = context.Model;
    }

    /// <summary>The entry PK is a sequential GUID with the NEWSEQUENTIALID() default (SDD-FIN-001 §2.1, §6.4).</summary>
    [Test]
    public void JournalEntryConfiguration_MapsIdAsSequentialGuid()
    {
        // Arrange
        IProperty id = _model.FindEntityType(typeof(JournalEntry))!.FindProperty(nameof(JournalEntry.Id))!;

        // Act
        string? defaultSql = id.GetDefaultValueSql();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(id.ClrType, Is.EqualTo(typeof(Guid)));
            Assert.That(id.IsPrimaryKey(), Is.True);
            Assert.That(defaultSql, Does.Contain("NEWSEQUENTIALID"));
        });
    }

    /// <summary>The entry RowVersion is configured as a rowversion concurrency token (SDD-FIN-001 §2.1, §6.4).</summary>
    [Test]
    public void JournalEntryConfiguration_ConfiguresRowVersionConcurrencyToken()
    {
        // Arrange
        IProperty rowVersion = _model.FindEntityType(typeof(JournalEntry))!
            .FindProperty(nameof(JournalEntry.RowVersion))!;

        // Act & Assert
        Assert.Multiple(() =>
        {
            Assert.That(rowVersion.IsConcurrencyToken, Is.True);
            Assert.That(rowVersion.ValueGenerated, Is.EqualTo(ValueGenerated.OnAddOrUpdate));
        });
    }

    /// <summary>Line debit/credit amounts map to decimal(18,2) (SDD-FIN-001 §2.2, §6.4).</summary>
    [Test]
    public void JournalEntryLineConfiguration_MapsAmountsAsDecimal18_2()
    {
        // Arrange
        IEntityType line = _model.FindEntityType(typeof(JournalEntryLine))!;

        // Act
        string? debitType = line.FindProperty(nameof(JournalEntryLine.DebitAmount))!.GetColumnType();
        string? creditType = line.FindProperty(nameof(JournalEntryLine.CreditAmount))!.GetColumnType();
        string? baseDebitType = line.FindProperty(nameof(JournalEntryLine.BaseDebitAmount))!.GetColumnType();
        string? baseCreditType = line.FindProperty(nameof(JournalEntryLine.BaseCreditAmount))!.GetColumnType();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(debitType, Is.EqualTo("decimal(18,2)"));
            Assert.That(creditType, Is.EqualTo("decimal(18,2)"));
            Assert.That(baseDebitType, Is.EqualTo("decimal(18,2)"));
            Assert.That(baseCreditType, Is.EqualTo("decimal(18,2)"));
        });
    }

    /// <summary>The line exchange rate maps to decimal(18,6) (SDD-FIN-001 §2.2, §6.4).</summary>
    [Test]
    public void JournalEntryLineConfiguration_MapsExchangeRateAsDecimal18_6()
    {
        // Arrange
        IProperty rate = _model.FindEntityType(typeof(JournalEntryLine))!
            .FindProperty(nameof(JournalEntryLine.ExchangeRate))!;

        // Act
        string? rateType = rate.GetColumnType();

        // Assert
        Assert.That(rateType, Is.EqualTo("decimal(18,6)"));
    }

    /// <summary>The Journal error map classifies ACCOUNT_NOT_POSTABLE as 409 Conflict (SDD-FIN-001 §4, §6.4).</summary>
    [Test]
    public void DefaultErrorCodeToStatusMap_MapsAccountNotPostableTo409()
    {
        // Arrange
        JournalErrorCodeToStatusMap map = new();

        // Act
        int status = map.MapToStatus(JournalErrorCodes.ACCOUNT_NOT_POSTABLE);

        // Assert
        Assert.That(status, Is.EqualTo(StatusCodes.Status409Conflict));
    }
}
