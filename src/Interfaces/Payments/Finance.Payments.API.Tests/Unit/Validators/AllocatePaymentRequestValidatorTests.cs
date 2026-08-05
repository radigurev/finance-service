using Finance.Common.ErrorCodes;
using Finance.Payments.API.Validators;
using Finance.ServiceModel.Payments;
using FluentValidation.Results;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Validators;

/// <summary>
/// Unit tests for <see cref="AllocatePaymentRequestValidator"/> and its per-item validator — the SDD-PAY-002 §3.1
/// SHAPE-only rules (§6.5). Everything that depends on another row or on aggregate state belongs to the §2.5 chain,
/// not here.
/// </summary>
[TestFixture]
[Category("SDD-PAY-002")]
public sealed class AllocatePaymentRequestValidatorTests
{
    private AllocatePaymentRequestValidator _sut = null!;

    /// <summary>Creates a fresh validator before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _sut = new AllocatePaymentRequestValidator();
    }

    [Test]
    public void Validate_ValidRequest_PassesEveryShapeRule()
    {
        // Arrange
        AllocatePaymentRequest request = RequestFor(Guid.NewGuid(), 100.00m);

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(result.IsValid, Is.True, string.Join(", ", result.Errors.Select(error => error.ErrorCode)));
    }

    [TestCase(0.00)]
    [TestCase(-1.00)]
    public void Validate_NonPositiveAmount_ReturnsInvalidPaymentAllocationAmount(decimal amount)
    {
        // Arrange
        AllocatePaymentRequest request = RequestFor(Guid.NewGuid(), amount);

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PaymentErrorCodes.INVALID_PAYMENT_ALLOCATION_AMOUNT));
    }

    [Test]
    public void Validate_AmountWithMoreThanTwoDecimals_ReturnsInvalidPaymentAllocationAmount()
    {
        // Arrange
        AllocatePaymentRequest request = RequestFor(Guid.NewGuid(), 100.005m);

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PaymentErrorCodes.INVALID_PAYMENT_ALLOCATION_AMOUNT));
    }

    [Test]
    public void Validate_EmptyItemList_ReturnsPaymentAllocationItemsRequired()
    {
        // Arrange
        AllocatePaymentRequest request = new()
        {
            Items = [],
            RowVersion = ValidRowVersion
        };

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ErrorCodes(result), Does.Contain(PaymentErrorCodes.PAYMENT_ALLOCATION_ITEMS_REQUIRED));
            Assert.That(
                ErrorCodes(result),
                Does.Not.Contain(PaymentErrorCodes.INVALID_PAYMENT_ALLOCATION_AMOUNT),
                "an omitted list must never be read as a request for automatic FIFO matching");
        });
    }

    [Test]
    public void Validate_MissingInvoiceId_ReturnsPaymentAllocationInvoiceRequired()
    {
        // Arrange
        AllocatePaymentRequest request = RequestFor(Guid.Empty, 100.00m);

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PaymentErrorCodes.PAYMENT_ALLOCATION_INVOICE_REQUIRED));
    }

    [TestCase("")]
    [TestCase("not-base64!!")]
    public void Validate_MalformedRowVersion_ReturnsConcurrentModification(string rowVersion)
    {
        // Arrange
        AllocatePaymentRequest request = new()
        {
            Items = [new AllocatePaymentItem { InvoiceId = Guid.NewGuid(), AllocatedAmount = 100.00m }],
            RowVersion = rowVersion
        };

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(CommonErrorCodes.CONCURRENT_MODIFICATION));
    }

    /// <summary>A valid base64 concurrency token for the shape-only rules.</summary>
    private static string ValidRowVersion => Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

    /// <summary>Builds a single-item allocation request.</summary>
    /// <param name="invoiceId">The invoice identifier.</param>
    /// <param name="amount">The allocated amount.</param>
    /// <returns>The request.</returns>
    private static AllocatePaymentRequest RequestFor(Guid invoiceId, decimal amount) => new()
    {
        Items = [new AllocatePaymentItem { InvoiceId = invoiceId, AllocatedAmount = amount }],
        RowVersion = ValidRowVersion
    };

    /// <summary>Projects a validation result onto its machine-readable error codes.</summary>
    /// <param name="result">The validation result.</param>
    /// <returns>The error codes raised.</returns>
    private static IReadOnlyList<string> ErrorCodes(ValidationResult result) =>
        [.. result.Errors.Select(error => error.ErrorCode)];
}
