using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Payments.API.Tests.Builders;
using Finance.Payments.API.Tests.Fixtures;
using Finance.Payments.API.Validators;
using Finance.ServiceModel.Payments;
using FluentValidation.Results;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Validators;

/// <summary>
/// Unit tests for <see cref="CreatePaymentRequestValidator"/> — the SDD-PAY-001 §3.1 field-level surface
/// (§6.3/§6.4). Every rule references a constant in <c>PaymentErrorCodes</c>, and the date bounds read the injected
/// <c>TimeProvider</c> rather than the wall clock.
/// </summary>
[TestFixture]
[Category("SDD-PAY-001")]
public sealed class CreatePaymentRequestValidatorTests
{
    private FixedTimeProvider _clock = null!;
    private CreatePaymentRequestValidator _sut = null!;

    /// <summary>Creates a fresh validator over a pinned clock before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _clock = new FixedTimeProvider();
        _sut = new CreatePaymentRequestValidator(_clock);
    }

    [Test]
    public void Validate_ValidRequest_PassesEveryFieldRule()
    {
        // Arrange
        CreatePaymentRequest request = CreatePaymentRequestBuilder.Create().Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(result.IsValid, Is.True, string.Join(", ", result.Errors.Select(error => error.ErrorCode)));
    }

    [Test]
    public void CreateDraft_UnknownDocumentType_ReturnsInvalidPaymentDocumentType()
    {
        // Arrange
        CreatePaymentRequest request = CreatePaymentRequestBuilder.Create()
            .WithDocumentType((PaymentDocumentType)99)
            .Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PaymentErrorCodes.INVALID_PAYMENT_DOCUMENT_TYPE));
    }

    [Test]
    public void Validate_MissingOrUnknownMethod_ReturnsInvalidPaymentMethod()
    {
        // Arrange
        CreatePaymentRequest missing = CreatePaymentRequestBuilder.Create()
            .WithMethod(default)
            .Build();
        CreatePaymentRequest unknown = CreatePaymentRequestBuilder.Create()
            .WithMethod((PaymentMethod)77)
            .Build();

        // Act
        ValidationResult missingResult = _sut.Validate(missing);
        ValidationResult unknownResult = _sut.Validate(unknown);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ErrorCodes(missingResult), Does.Contain(PaymentErrorCodes.INVALID_PAYMENT_METHOD));
            Assert.That(ErrorCodes(unknownResult), Does.Contain(PaymentErrorCodes.INVALID_PAYMENT_METHOD));
        });
    }

    [Test]
    public void Validate_MissingCounterparty_ReturnsPaymentCounterpartyRequired()
    {
        // Arrange
        CreatePaymentRequest request = CreatePaymentRequestBuilder.Create()
            .WithCounterpartyId(Guid.Empty)
            .Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PaymentErrorCodes.PAYMENT_COUNTERPARTY_REQUIRED));
    }

    [TestCase("")]
    [TestCase("bg")]
    [TestCase("BGNN")]
    [TestCase("BG1")]
    public void Validate_InvalidCurrencyCode_ReturnsInvalidPaymentCurrency(string currencyCode)
    {
        // Arrange
        CreatePaymentRequest request = CreatePaymentRequestBuilder.Create()
            .WithCurrencyCode(currencyCode)
            .Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PaymentErrorCodes.INVALID_PAYMENT_CURRENCY));
    }

    [Test]
    public void Validate_MissingPaymentDate_ReturnsInvalidPaymentDate()
    {
        // Arrange
        CreatePaymentRequest request = CreatePaymentRequestBuilder.Create()
            .WithPaymentDate(default)
            .Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PaymentErrorCodes.INVALID_PAYMENT_DATE));
    }

    [Test]
    public void Validate_FuturePaymentDate_ReturnsInvalidPaymentDate()
    {
        // Arrange
        CreatePaymentRequest request = CreatePaymentRequestBuilder.Create()
            .WithPaymentDate(_clock.UtcNow.AddDays(1))
            .Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PaymentErrorCodes.INVALID_PAYMENT_DATE));
    }

    [Test]
    public void Validate_PaymentDateToday_IsAccepted()
    {
        // Arrange
        CreatePaymentRequest request = CreatePaymentRequestBuilder.Create()
            .WithPaymentDate(_clock.UtcNow)
            .Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Not.Contain(PaymentErrorCodes.INVALID_PAYMENT_DATE));
    }

    [TestCase(0)]
    [TestCase(-5)]
    public void Validate_NonPositiveSettlementAccountId_ReturnsPaymentSettlementAccountRequired(
        int settlementAccountId)
    {
        // Arrange
        CreatePaymentRequest request = CreatePaymentRequestBuilder.Create()
            .WithSettlementAccountId(settlementAccountId)
            .Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PaymentErrorCodes.PAYMENT_SETTLEMENT_ACCOUNT_REQUIRED));
    }

    [Test]
    public void Validate_BankReferenceOver64Chars_ReturnsInvalidPaymentBankReference()
    {
        // Arrange
        CreatePaymentRequest request = CreatePaymentRequestBuilder.Create()
            .WithBankReference(new string('X', 65))
            .Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PaymentErrorCodes.INVALID_PAYMENT_BANK_REFERENCE));
    }

    [Test]
    public void Validate_NonPositiveExchangeRate_ReturnsInvalidPaymentExchangeRate()
    {
        // Arrange
        CreatePaymentRequest request = CreatePaymentRequestBuilder.Create()
            .WithExchangeRate(0m)
            .Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PaymentErrorCodes.INVALID_PAYMENT_EXCHANGE_RATE));
    }

    /// <summary>Projects a validation result onto its machine-readable error codes.</summary>
    /// <param name="result">The validation result.</param>
    /// <returns>The error codes raised.</returns>
    private static IReadOnlyList<string> ErrorCodes(ValidationResult result) =>
        [.. result.Errors.Select(error => error.ErrorCode)];
}
