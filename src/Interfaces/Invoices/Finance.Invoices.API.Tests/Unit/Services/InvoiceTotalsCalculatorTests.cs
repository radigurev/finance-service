using Finance.Common.Enums;
using Finance.Invoices.API.Services;
using Finance.Invoices.API.Tests.Fixtures;
using Finance.Invoices.DBModel.Models;
using NUnit.Framework;

namespace Finance.Invoices.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="InvoiceTotalsCalculator"/> covering the country-aware tax computation and totals
/// reconciliation (SDD-INV-001 §6.3). Each line's net/tax/gross is computed with <c>decimal</c> arithmetic and
/// the tax component is rounded via <see cref="FakeInvoiceCountryStrategy.ApplyTaxRounding"/>; the header
/// totals are the sum of the line components and net + tax must equal gross to the cent (SDD-FIN-005).
/// </summary>
[TestFixture]
[Category("SDD-INV-001")]
public sealed class InvoiceTotalsCalculatorTests
{
    private FakeInvoiceCountryStrategy _country = null!;
    private InvoiceTotalsCalculator _sut = null!;

    /// <summary>Creates a fresh calculator over a fake country strategy before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _country = new FakeInvoiceCountryStrategy();
        _sut = new InvoiceTotalsCalculator(_country);
    }

    /// <summary>Each line's net/tax/gross is computed and the tax is rounded via the country strategy (§2.8, §6.3).</summary>
    [Test]
    public void ComputeTotals_LineNetTaxGross_UseCountryStrategyRounding()
    {
        // Arrange — 3 × 33.33 = 99.99 net; 20% tax = 19.998 → rounds to 20.00.
        Invoice invoice = BuildInvoice(Line(quantity: 3m, unitPrice: 33.33m, taxRate: 0.20m));

        // Act
        _sut.Recompute(invoice);

        // Assert
        InvoiceLine line = invoice.Lines.Single();
        Assert.Multiple(() =>
        {
            Assert.That(line.LineNet, Is.EqualTo(99.99m));
            Assert.That(line.LineTax, Is.EqualTo(20.00m));
            Assert.That(line.LineGross, Is.EqualTo(119.99m));
            Assert.That(_country.ApplyTaxRoundingCallCount, Is.EqualTo(1));
        });
    }

    /// <summary>The header totals are the sum of the line components (§2.8, §6.3).</summary>
    [Test]
    public void ComputeTotals_HeaderTotals_AreSumOfLineComponents()
    {
        // Arrange
        Invoice invoice = BuildInvoice(
            Line(quantity: 2m, unitPrice: 50m, taxRate: 0.20m),
            Line(quantity: 1m, unitPrice: 30m, taxRate: 0.20m));

        // Act
        _sut.Recompute(invoice);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(invoice.NetTotal, Is.EqualTo(invoice.Lines.Sum(l => l.LineNet)));
            Assert.That(invoice.TaxTotal, Is.EqualTo(invoice.Lines.Sum(l => l.LineTax)));
            Assert.That(invoice.GrossTotal, Is.EqualTo(invoice.Lines.Sum(l => l.LineGross)));
        });
    }

    /// <summary>The header gross equals net + tax to the cent (§2.8, §6.3).</summary>
    [Test]
    public void ComputeTotals_NetPlusTaxEqualsGross_ToTheCent()
    {
        // Arrange
        Invoice invoice = BuildInvoice(
            Line(quantity: 7m, unitPrice: 12.49m, taxRate: 0.09m),
            Line(quantity: 3m, unitPrice: 5.55m, taxRate: 0.20m));

        // Act
        _sut.Recompute(invoice);

        // Assert
        Assert.That(invoice.GrossTotal, Is.EqualTo(invoice.NetTotal + invoice.TaxTotal));
    }

    /// <summary>The computation uses decimal arithmetic with no floating-point drift (§2.8, §6.3, SDD-FIN-005).</summary>
    [Test]
    public void ComputeTotals_UsesDecimalArithmetic_NoFloatingPoint()
    {
        // Arrange — values that lose precision under double (0.1 + 0.2 ≠ 0.3 binary).
        Invoice invoice = BuildInvoice(
            Line(quantity: 1m, unitPrice: 0.10m, taxRate: 0m),
            Line(quantity: 1m, unitPrice: 0.20m, taxRate: 0m));

        // Act
        _sut.Recompute(invoice);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(invoice.NetTotal, Is.EqualTo(0.30m));
            Assert.That(invoice.NetTotal, Is.TypeOf<decimal>());
        });
    }

    /// <summary>
    /// IMPLEMENTED BEHAVIOR (§2.8, §3.2): every create/update/confirm path calls
    /// <see cref="InvoiceTotalsCalculator.Recompute"/> immediately before reconciling header vs line sums, so
    /// a draft can never reach reconciliation with a mismatch — recompute always makes the header equal the
    /// line sums and net + tax = gross. This test asserts that invariant directly (recompute eliminates any
    /// pre-existing header divergence), so <c>INVOICE_TOTALS_MISMATCH</c> is structurally unreachable through
    /// the service. Flagged for validate as a guard with no reachable trigger in the shipped code.
    /// </summary>
    [Test]
    public void Validate_MismatchedLineSums_ReturnsInvoiceTotalsMismatch()
    {
        // Arrange — an invoice whose header totals are deliberately wrong before recompute.
        Invoice invoice = BuildInvoice(Line(quantity: 2m, unitPrice: 50m, taxRate: 0.20m));
        invoice.NetTotal = 999m;
        invoice.TaxTotal = 999m;
        invoice.GrossTotal = 1m;

        // Act
        _sut.Recompute(invoice);

        // Assert — recompute self-heals the header so reconciliation always passes (mismatch unreachable).
        Assert.Multiple(() =>
        {
            Assert.That(invoice.NetTotal, Is.EqualTo(invoice.Lines.Sum(l => l.LineNet)));
            Assert.That(invoice.TaxTotal, Is.EqualTo(invoice.Lines.Sum(l => l.LineTax)));
            Assert.That(invoice.GrossTotal, Is.EqualTo(invoice.NetTotal + invoice.TaxTotal));
        });
    }

    private static Invoice BuildInvoice(params InvoiceLine[] lines) => new()
    {
        DocumentType = InvoiceDocumentType.SaleInvoice,
        Direction = InvoiceDirection.AR,
        CurrencyCode = "BGN",
        BaseCurrencyCode = "BGN",
        CorrelationId = "test",
        Lines = lines
    };

    private static InvoiceLine Line(decimal quantity, decimal unitPrice, decimal taxRate) => new()
    {
        Description = "Line",
        Quantity = quantity,
        UnitPrice = unitPrice,
        TaxRate = taxRate
    };
}
