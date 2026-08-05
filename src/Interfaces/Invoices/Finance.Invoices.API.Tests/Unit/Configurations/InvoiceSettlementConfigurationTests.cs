using Finance.Invoices.DBModel;
using Finance.Invoices.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NUnit.Framework;

namespace Finance.Invoices.API.Tests.Unit.Configurations;

/// <summary>
/// Unit tests for the FOUR columns the settlement amendment added to the <see cref="Invoice"/> mapping
/// (SDD-INV-001 §2.14, §6.7). They inspect the built SQL Server model so the production column types, defaults,
/// and nullability are asserted directly rather than inferred from the SQLite test shape.
/// <para><c>ExchangeRate</c> is <c>DECIMAL(18,6)</c> — the rate precision SDD-FIN-005 mandates — while the
/// settled amount is <c>DECIMAL(18,2)</c>; a rate stored at amount precision would silently truncate every
/// non-base-currency booking rate. <c>LastSettlementAppliedAt</c> is a PLAIN nullable
/// <c>datetimeoffset</c> with NO <c>SYSDATETIMEOFFSET()</c> default, because its value is the event's
/// <c>OccurredAt</c> and never the row's write time.</para>
/// </summary>
[TestFixture]
[Category("SDD-INV-001")]
[Category("SDD-PAY-002")]
public sealed class InvoiceSettlementConfigurationTests
{
    private IModel _model = null!;

    /// <summary>Builds the production SQL Server model once for inspection.</summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        DbContextOptions<InvoicesDbContext> options = new DbContextOptionsBuilder<InvoicesDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=model-only")
            .Options;

        using InvoicesDbContext context = new(options);
        _model = context.Model;
    }

    /// <summary>
    /// The settled amount is a required <c>decimal(18,2)</c> defaulting to <c>0</c>, and the derived settlement
    /// status is a required string-converted enum capped at 20 characters (§2.14).
    /// </summary>
    [Test]
    public void InvoiceConfiguration_ConfiguresSettledAmountAsDecimal182_AndSettlementStatusAsString()
    {
        // Arrange
        IEntityType invoice = _model.FindEntityType(typeof(Invoice))!;

        // Act
        IProperty settledAmount = invoice.FindProperty(nameof(Invoice.SettledAmount))!;
        IProperty settlementStatus = invoice.FindProperty(nameof(Invoice.SettlementStatus))!;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(settledAmount.GetColumnType(), Is.EqualTo("decimal(18,2)"));
            Assert.That(settledAmount.IsNullable, Is.False);
            Assert.That(settledAmount.GetDefaultValue(), Is.EqualTo(0m));
            Assert.That(settlementStatus.GetProviderClrType(), Is.EqualTo(typeof(string)));
            Assert.That(settlementStatus.GetMaxLength(), Is.EqualTo(20));
            Assert.That(settlementStatus.IsNullable, Is.False);
        });
    }

    /// <summary>
    /// The frozen booking rate is a required <c>decimal(18,6)</c> defaulting to <c>1.000000</c> — RATE precision,
    /// not amount precision (§2.14, SDD-FIN-005).
    /// </summary>
    [Test]
    public void InvoiceConfiguration_ConfiguresExchangeRateAsDecimal186()
    {
        // Arrange
        IProperty exchangeRate = _model.FindEntityType(typeof(Invoice))!
            .FindProperty(nameof(Invoice.ExchangeRate))!;

        // Act
        string? columnType = exchangeRate.GetColumnType();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(columnType, Is.EqualTo("decimal(18,6)"));
            Assert.That(exchangeRate.IsNullable, Is.False);
            Assert.That(exchangeRate.GetDefaultValue(), Is.EqualTo(1.000000m));
            Assert.That(exchangeRate.ClrType, Is.EqualTo(typeof(decimal)));
        });
    }

    /// <summary>
    /// The ordering token is a plain nullable <c>datetimeoffset</c> with NO default, so an unapplied invoice reads
    /// NULL and the first event always wins (§2.14, §2.15 step 5).
    /// </summary>
    [Test]
    public void InvoiceConfiguration_ConfiguresLastSettlementAppliedAtAsNullableDateTimeOffset_WithoutDefault()
    {
        // Arrange
        IProperty lastApplied = _model.FindEntityType(typeof(Invoice))!
            .FindProperty(nameof(Invoice.LastSettlementAppliedAt))!;

        // Act
        string? defaultSql = lastApplied.GetDefaultValueSql();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(lastApplied.ClrType, Is.EqualTo(typeof(DateTimeOffset?)));
            Assert.That(lastApplied.IsNullable, Is.True);
            Assert.That(defaultSql, Is.Null);
            Assert.That(lastApplied.GetDefaultValue(), Is.Null);
        });
    }

    /// <summary>
    /// The settlement read pattern is supported by <c>IX_Invoices_SettlementStatus</c>, so "unsettled invoices"
    /// is a server-side filter rather than a client-side scan (§2.14, SDD-INFRA-005).
    /// </summary>
    [Test]
    public void InvoiceConfiguration_IndexesSettlementStatus_ForTheSettlementReadPatterns()
    {
        // Arrange
        IEntityType invoice = _model.FindEntityType(typeof(Invoice))!;

        // Act
        IIndex index = invoice.GetIndexes()
            .Single(candidate => candidate.GetDatabaseName() == "IX_Invoices_SettlementStatus");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(
                index.Properties.Select(property => property.Name),
                Is.EqualTo(new[] { nameof(Invoice.SettlementStatus) }));
            Assert.That(index.IsUnique, Is.False);
        });
    }
}
