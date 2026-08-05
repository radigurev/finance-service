using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Payments.API.Services;
using Finance.Payments.API.Tests.Fixtures;
using Finance.Payments.API.Validators;
using Finance.ServiceModel.Payments;
using FluentValidation.Results;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Validators;

/// <summary>
/// Unit tests for the three aging FluentValidation validators — the SDD-PAY-003 §3.1 field-level surface (§6.5). All
/// of their codes are 400 validation codes, and the date rules read the injected <c>TimeProvider</c> rather than the
/// wall clock.
/// </summary>
[TestFixture]
[Category("SDD-PAY-003")]
public sealed class AgingQueryValidatorTests
{
    private static readonly DateTimeOffset Today = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

    private FixedTimeProvider _clock = null!;
    private OpenItemQueryRequestValidator _openItems = null!;
    private AgingReportQueryRequestValidator _report = null!;
    private CounterpartyBalanceQueryRequestValidator _balances = null!;

    /// <summary>Creates fresh validators over a pinned clock before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _clock = new FixedTimeProvider(Today);
        _openItems = new OpenItemQueryRequestValidator(_clock);
        _report = new AgingReportQueryRequestValidator(_clock, new AgingBucketCalculator());
        _balances = new CounterpartyBalanceQueryRequestValidator(_clock);
    }

    [Test]
    public void Validate_ValidQueries_PassEveryFieldRule()
    {
        // Arrange
        OpenItemQueryRequest openItems = new() { AsOfDate = Today, Direction = nameof(InvoiceDirection.AR) };
        AgingReportQueryRequest report = new() { AsOfDate = Today, Direction = nameof(InvoiceDirection.AP) };
        CounterpartyBalanceQueryRequest balances = new()
        {
            AsOfDate = Today,
            Direction = nameof(InvoiceDirection.AR),
            CurrencyCode = "EUR"
        };

        // Act
        ValidationResult openItemsResult = _openItems.Validate(openItems);
        ValidationResult reportResult = _report.Validate(report);
        ValidationResult balancesResult = _balances.Validate(balances);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(openItemsResult.IsValid, Is.True, Codes(openItemsResult));
            Assert.That(reportResult.IsValid, Is.True, Codes(reportResult));
            Assert.That(balancesResult.IsValid, Is.True, Codes(balancesResult));
        });
    }

    [Test]
    public void Validate_OpenItemsWithoutAsOfDate_IsAccepted_DefaultsToToday()
    {
        // Arrange
        OpenItemQueryRequest query = new();

        // Act
        ValidationResult result = _openItems.Validate(query);

        // Assert
        Assert.That(result.IsValid, Is.True, Codes(result));
    }

    [Test]
    public void Validate_ReportWithoutAsOfDate_ReturnsInvalidAgingAsOfDate()
    {
        // Arrange
        AgingReportQueryRequest report = new() { Direction = nameof(InvoiceDirection.AR) };
        CounterpartyBalanceQueryRequest balances = new() { Direction = nameof(InvoiceDirection.AR) };

        // Act
        ValidationResult reportResult = _report.Validate(report);
        ValidationResult balancesResult = _balances.Validate(balances);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ErrorCodes(reportResult), Does.Contain(PaymentErrorCodes.INVALID_AGING_AS_OF_DATE));
            Assert.That(ErrorCodes(balancesResult), Does.Contain(PaymentErrorCodes.INVALID_AGING_AS_OF_DATE));
        });
    }

    [Test]
    public void Validate_FutureAsOfDate_ReturnsInvalidAgingAsOfDate()
    {
        // Arrange
        AgingReportQueryRequest report = new()
        {
            AsOfDate = Today.AddDays(1),
            Direction = nameof(InvoiceDirection.AR)
        };

        // Act
        ValidationResult result = _report.Validate(report);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PaymentErrorCodes.INVALID_AGING_AS_OF_DATE));
    }

    [Test]
    public void Validate_NonAscendingBuckets_ReturnsInvalidAgingBuckets()
    {
        // Arrange
        AgingReportQueryRequest report = new()
        {
            AsOfDate = Today,
            Direction = nameof(InvoiceDirection.AR),
            Buckets = [60, 30, 90]
        };

        // Act
        ValidationResult result = _report.Validate(report);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PaymentErrorCodes.INVALID_AGING_BUCKETS));
    }

    [Test]
    public void Validate_EmptyCounterpartyId_ReturnsInvalidCounterpartyId()
    {
        // Arrange
        OpenItemQueryRequest query = new() { AsOfDate = Today, CounterpartyId = Guid.Empty };

        // Act
        ValidationResult result = _openItems.Validate(query);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PaymentErrorCodes.INVALID_COUNTERPARTY_ID));
    }

    [Test]
    public void Validate_MalformedCurrencyCode_ReturnsInvalidAgingCurrency()
    {
        // Arrange
        OpenItemQueryRequest query = new() { AsOfDate = Today, CurrencyCode = "eur" };

        // Act
        ValidationResult result = _openItems.Validate(query);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PaymentErrorCodes.INVALID_AGING_CURRENCY));
    }

    /// <summary>Projects a validation result onto its machine-readable error codes.</summary>
    /// <param name="result">The validation result.</param>
    /// <returns>The error codes raised.</returns>
    private static IReadOnlyList<string> ErrorCodes(ValidationResult result) =>
        [.. result.Errors.Select(error => error.ErrorCode)];

    /// <summary>Renders a validation result's error codes for an assertion message.</summary>
    /// <param name="result">The validation result.</param>
    /// <returns>The comma-separated codes.</returns>
    private static string Codes(ValidationResult result) => string.Join(", ", ErrorCodes(result));
}
