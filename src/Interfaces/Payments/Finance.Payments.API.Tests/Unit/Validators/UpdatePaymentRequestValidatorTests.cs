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
/// Unit tests for <see cref="UpdatePaymentRequestValidator"/> — the SDD-PAY-001 §3.1 field-level surface of the
/// UPDATE path (§6.4). It repeats the create surface and adds the <c>RowVersion</c> concurrency token. Every
/// rule references a constant in <c>PaymentErrorCodes</c> or <c>CommonErrorCodes</c>, and the date bounds read
/// the injected <c>TimeProvider</c> rather than the wall clock.
/// <para>The "document type unchanged" assertion (§3.2), the <c>Draft</c>-only immutability guard (§2.6), and the
/// base64 DECODE of the token all run in <c>PaymentService</c>, not here; they are pinned by
/// <see cref="Unit.Services.PaymentServiceCrudTests"/>.</para>
/// </summary>
[TestFixture]
[Category("SDD-PAY-001")]
public sealed class UpdatePaymentRequestValidatorTests
{
    private FixedTimeProvider _clock = null!;
    private UpdatePaymentRequestValidator _sut = null!;

    /// <summary>Creates a fresh validator over a pinned clock before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _clock = new FixedTimeProvider();
        _sut = new UpdatePaymentRequestValidator(_clock);
    }

    [Test]
    public void Validate_ValidRequest_PassesEveryFieldRule()
    {
        // Arrange
        UpdatePaymentRequest request = UpdatePaymentRequestBuilder.Create().Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(result.IsValid, Is.True, string.Join(", ", result.Errors.Select(error => error.ErrorCode)));
    }

    [Test]
    public void Constructor_NullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.That(
            () => new UpdatePaymentRequestValidator(null!),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Validate_UnknownDocumentType_ReturnsInvalidPaymentDocumentType()
    {
        // Arrange
        UpdatePaymentRequest request = UpdatePaymentRequestBuilder.Create()
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
        UpdatePaymentRequest missing = UpdatePaymentRequestBuilder.Create()
            .WithMethod(default)
            .Build();
        UpdatePaymentRequest unknown = UpdatePaymentRequestBuilder.Create()
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
        UpdatePaymentRequest request = UpdatePaymentRequestBuilder.Create()
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
        UpdatePaymentRequest request = UpdatePaymentRequestBuilder.Create()
            .WithCurrencyCode(currencyCode)
            .Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PaymentErrorCodes.INVALID_PAYMENT_CURRENCY));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Validate_NonPositiveAmount_ReturnsInvalidPaymentAmount(decimal amount)
    {
        // Arrange
        UpdatePaymentRequest request = UpdatePaymentRequestBuilder.Create()
            .WithAmount(amount)
            .Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PaymentErrorCodes.INVALID_PAYMENT_AMOUNT));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Validate_NonPositiveExchangeRate_ReturnsInvalidPaymentExchangeRate(decimal exchangeRate)
    {
        // Arrange
        UpdatePaymentRequest request = UpdatePaymentRequestBuilder.Create()
            .WithExchangeRate(exchangeRate)
            .Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PaymentErrorCodes.INVALID_PAYMENT_EXCHANGE_RATE));
    }

    [Test]
    public void Validate_MissingPaymentDate_ReturnsInvalidPaymentDate()
    {
        // Arrange
        UpdatePaymentRequest request = UpdatePaymentRequestBuilder.Create()
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
        UpdatePaymentRequest request = UpdatePaymentRequestBuilder.Create()
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
        UpdatePaymentRequest request = UpdatePaymentRequestBuilder.Create()
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
        UpdatePaymentRequest request = UpdatePaymentRequestBuilder.Create()
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
        UpdatePaymentRequest request = UpdatePaymentRequestBuilder.Create()
            .WithBankReference(new string('X', 65))
            .Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PaymentErrorCodes.INVALID_PAYMENT_BANK_REFERENCE));
    }

    [Test]
    public void Validate_BankReferenceAtExactly64Chars_IsAccepted()
    {
        // Arrange
        UpdatePaymentRequest request = UpdatePaymentRequestBuilder.Create()
            .WithBankReference(new string('X', 64))
            .Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Not.Contain(PaymentErrorCodes.INVALID_PAYMENT_BANK_REFERENCE));
    }

    [Test]
    public void Validate_NullBankReference_IsAccepted()
    {
        // Arrange
        UpdatePaymentRequest request = UpdatePaymentRequestBuilder.Create()
            .WithBankReference(null)
            .Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(result.IsValid, Is.True, "the bank reference is optional");
    }

    [TestCase("")]
    [TestCase(" ")]
    public void Validate_MissingRowVersion_ReturnsConcurrentModification(string rowVersion)
    {
        // Arrange
        UpdatePaymentRequest request = UpdatePaymentRequestBuilder.Create()
            .WithRowVersion(rowVersion)
            .Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(CommonErrorCodes.CONCURRENT_MODIFICATION));
    }

    [Test]
    public void Validate_WellFormedBase64RowVersion_IsAccepted()
    {
        // Arrange
        UpdatePaymentRequest request = UpdatePaymentRequestBuilder.Create()
            .WithRowVersion(Convert.ToBase64String(new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 }))
            .Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Not.Contain(CommonErrorCodes.CONCURRENT_MODIFICATION));
    }

    /// <summary>Projects a validation result onto its machine-readable error codes.</summary>
    /// <param name="result">The validation result.</param>
    /// <returns>The error codes raised.</returns>
    private static IReadOnlyList<string> ErrorCodes(ValidationResult result) =>
        [.. result.Errors.Select(error => error.ErrorCode)];
}
