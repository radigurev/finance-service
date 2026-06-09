using Finance.Common.ErrorCodes;
using Finance.Invoices.API.Tests.Builders;
using Finance.Invoices.API.Tests.Fixtures;
using Finance.Invoices.API.Validators;
using Finance.ServiceModel.Invoices;
using FluentValidation.Results;
using NUnit.Framework;

namespace Finance.Invoices.API.Tests.Unit.Validators;

/// <summary>
/// Unit tests for <see cref="CreateInvoiceRequestValidator"/> covering the field-level shape rules and the
/// per-line tax-rate validity that delegates to <see cref="FakeInvoiceCountryStrategy.IsValidTaxRate"/>
/// (SDD-INV-001 §3.1, §6.3). The country owns the legal rate set, so a negative or unrecognized rate yields
/// <c>INVALID_INVOICE_TAX_RATE</c> while the core never hard-codes a VAT rate.
/// </summary>
[TestFixture]
[Category("SDD-INV-001")]
public sealed class CreateInvoiceRequestValidatorTests
{
    private FakeInvoiceCountryStrategy _country = null!;
    private CreateInvoiceRequestValidator _sut = null!;

    /// <summary>Creates a fresh validator over a fake country strategy before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _country = new FakeInvoiceCountryStrategy();
        _sut = new CreateInvoiceRequestValidator(_country);
    }

    /// <summary>A negative tax rate is rejected with INVALID_INVOICE_TAX_RATE (§2.8, §3.1, §6.3).</summary>
    [Test]
    public void Validate_NegativeTaxRate_ReturnsInvalidInvoiceTaxRate()
    {
        // Arrange
        CreateInvoiceRequest request = CreateInvoiceRequestBuilder.Create()
            .WithLines(InvoiceLineRequestBuilder.Create().WithTaxRate(-0.05m).Build())
            .Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.Errors.Select(error => error.ErrorCode),
                Has.Member(InvoiceErrorCodes.INVALID_INVOICE_TAX_RATE));
        });
    }

    /// <summary>A rate the country does not recognize is rejected with INVALID_INVOICE_TAX_RATE (§2.8, §3.1, §6.3).</summary>
    [Test]
    public void Validate_UnrecognizedTaxRate_ReturnsInvalidInvoiceTaxRate()
    {
        // Arrange
        _country.RecognizedRates = new HashSet<decimal> { 0.20m, 0.09m, 0m };
        CreateInvoiceRequest request = CreateInvoiceRequestBuilder.Create()
            .WithLines(InvoiceLineRequestBuilder.Create().WithTaxRate(0.15m).Build())
            .Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(
            result.Errors.Select(error => error.ErrorCode),
            Has.Member(InvoiceErrorCodes.INVALID_INVOICE_TAX_RATE));
    }

    /// <summary>A manual create with no lines is rejected with INVOICE_LINES_REQUIRED (§3.1, §6.3).</summary>
    [Test]
    public void Validate_MissingLines_ReturnsInvoiceLinesRequired()
    {
        // Arrange
        CreateInvoiceRequest request = CreateInvoiceRequestBuilder.Create().WithNoLines().Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(
            result.Errors.Select(error => error.ErrorCode),
            Has.Member(InvoiceErrorCodes.INVOICE_LINES_REQUIRED));
    }

    /// <summary>A due date earlier than the issue date is rejected with INVALID_INVOICE_DUE_DATE (§3.1, §6.3).</summary>
    [Test]
    public void Validate_DueDateBeforeIssueDate_ReturnsInvalidInvoiceDueDate()
    {
        // Arrange
        CreateInvoiceRequest request = CreateInvoiceRequestBuilder.Create()
            .WithIssueDate(new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero))
            .WithDueDate(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero))
            .Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(
            result.Errors.Select(error => error.ErrorCode),
            Has.Member(InvoiceErrorCodes.INVALID_INVOICE_DUE_DATE));
    }

    /// <summary>A valid request passes all shape rules (§3.1, §6.3).</summary>
    [Test]
    public void Validate_ValidRequest_Passes()
    {
        // Arrange
        CreateInvoiceRequest request = CreateInvoiceRequestBuilder.Create().Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }
}
