using System.Reflection;
using Finance.Common.ErrorCodes;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="PaymentErrorCodes"/> (SDD-PAY-001 §4/§6.4, SDD-PAY-002 §4/§6.5, SDD-PAY-003 §4/§6.5).
/// Every documented code must exist as a constant whose VALUE equals its NAME, so the ProblemDetails <c>title</c>
/// and the FluentValidation <c>.WithErrorCode(...)</c> references can never drift apart, and no code may be a raw
/// string literal.
/// </summary>
[TestFixture]
public sealed class PaymentErrorCodesTests
{
    private static readonly string[] LifecycleCodes =
    [
        nameof(PaymentErrorCodes.PAYMENT_NOT_FOUND),
        nameof(PaymentErrorCodes.PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND),
        nameof(PaymentErrorCodes.PAYMENT_SETTLEMENT_ACCOUNT_INACTIVE),
        nameof(PaymentErrorCodes.PAYMENT_SETTLEMENT_ACCOUNT_REQUIRED),
        nameof(PaymentErrorCodes.INVALID_PAYMENT_DOCUMENT_TYPE),
        nameof(PaymentErrorCodes.INVALID_PAYMENT_METHOD),
        nameof(PaymentErrorCodes.PAYMENT_COUNTERPARTY_REQUIRED),
        nameof(PaymentErrorCodes.INVALID_PAYMENT_CURRENCY),
        nameof(PaymentErrorCodes.INVALID_PAYMENT_AMOUNT),
        nameof(PaymentErrorCodes.INVALID_PAYMENT_EXCHANGE_RATE),
        nameof(PaymentErrorCodes.INVALID_PAYMENT_DATE),
        nameof(PaymentErrorCodes.INVALID_PAYMENT_BANK_REFERENCE),
        nameof(PaymentErrorCodes.PAYMENT_BASE_AMOUNT_MISMATCH),
        nameof(PaymentErrorCodes.PAYMENT_CANCEL_REASON_REQUIRED),
        nameof(PaymentErrorCodes.PAYMENT_REVERSE_REASON_REQUIRED),
        nameof(PaymentErrorCodes.PAYMENT_NOT_DRAFT),
        nameof(PaymentErrorCodes.PAYMENT_NOT_CONFIRMED),
        nameof(PaymentErrorCodes.PAYMENT_POSTING_PENDING),
        nameof(PaymentErrorCodes.PAYMENT_POSTED_IMMUTABLE),
        nameof(PaymentErrorCodes.INVALID_PAYMENT_STATE_TRANSITION),
        nameof(PaymentErrorCodes.PAYMENT_PERIOD_CLOSED),
        nameof(PaymentErrorCodes.PAYMENT_DUPLICATE_DOCUMENT_NUMBER),
        nameof(PaymentErrorCodes.PAYMENT_DATE_YEAR_MISMATCH),
        nameof(PaymentErrorCodes.PAYMENT_HAS_ALLOCATIONS)
    ];

    private static readonly string[] AllocationCodes =
    [
        nameof(PaymentErrorCodes.PAYMENT_ALLOCATION_NOT_FOUND),
        nameof(PaymentErrorCodes.PAYMENT_ALLOCATION_INVOICE_NOT_FOUND),
        nameof(PaymentErrorCodes.PAYMENT_ALLOCATION_ITEMS_REQUIRED),
        nameof(PaymentErrorCodes.PAYMENT_ALLOCATION_INVOICE_REQUIRED),
        nameof(PaymentErrorCodes.INVALID_PAYMENT_ALLOCATION_AMOUNT),
        nameof(PaymentErrorCodes.PAYMENT_NOT_ALLOCATABLE),
        nameof(PaymentErrorCodes.PAYMENT_ALLOCATION_INVOICE_NOT_ELIGIBLE),
        nameof(PaymentErrorCodes.PAYMENT_ALLOCATION_EXCEEDS_PAYMENT),
        nameof(PaymentErrorCodes.PAYMENT_ALLOCATION_EXCEEDS_OUTSTANDING),
        nameof(PaymentErrorCodes.PAYMENT_ALLOCATION_DIRECTION_MISMATCH),
        nameof(PaymentErrorCodes.PAYMENT_ALLOCATION_COUNTERPARTY_MISMATCH),
        nameof(PaymentErrorCodes.PAYMENT_ALLOCATION_CURRENCY_MISMATCH),
        nameof(PaymentErrorCodes.PAYMENT_ALLOCATION_CONTROL_ACCOUNT_MISMATCH),
        nameof(PaymentErrorCodes.PAYMENT_ALLOCATION_DUPLICATE)
    ];

    private static readonly string[] AgingCodes =
    [
        nameof(PaymentErrorCodes.INVALID_AGING_AS_OF_DATE),
        nameof(PaymentErrorCodes.INVALID_AGING_DIRECTION),
        nameof(PaymentErrorCodes.INVALID_AGING_BUCKETS),
        nameof(PaymentErrorCodes.INVALID_COUNTERPARTY_ID),
        nameof(PaymentErrorCodes.INVALID_AGING_CURRENCY)
    ];

    [Test]
    [Category("SDD-PAY-001")]
    public void PaymentErrorCodes_DefinesAllPaymentCodes()
    {
        // Arrange
        IReadOnlyDictionary<string, string> declared = DeclaredConstants();

        // Act
        IReadOnlyList<string> missing = [.. LifecycleCodes.Where(code => !declared.ContainsKey(code))];

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(missing, Is.Empty);
            Assert.That(
                LifecycleCodes.Where(declared.ContainsKey).All(code => declared[code] == code),
                Is.True,
                "every constant's value must equal its name");
        });
    }

    [Test]
    [Category("SDD-PAY-002")]
    public void PaymentErrorCodes_DefinesAllAllocationCodes()
    {
        // Arrange
        IReadOnlyDictionary<string, string> declared = DeclaredConstants();

        // Act
        IReadOnlyList<string> missing = [.. AllocationCodes.Where(code => !declared.ContainsKey(code))];

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(missing, Is.Empty);
            Assert.That(
                AllocationCodes.Where(declared.ContainsKey).All(code => declared[code] == code),
                Is.True);
        });
    }

    [Test]
    [Category("SDD-PAY-003")]
    public void PaymentErrorCodes_DefinesAllAgingCodes()
    {
        // Arrange
        IReadOnlyDictionary<string, string> declared = DeclaredConstants();

        // Act
        IReadOnlyList<string> missing = [.. AgingCodes.Where(code => !declared.ContainsKey(code))];

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(missing, Is.Empty);
            Assert.That(AgingCodes.Where(declared.ContainsKey).All(code => declared[code] == code), Is.True);
            Assert.That(
                declared.Keys,
                Does.Not.Contain("COUNTERPARTY_NOT_FOUND"),
                "an unknown counterparty is an empty 200, not an error (SDD-PAY-003 §2.10)");
            Assert.That(declared.Keys, Does.Not.Contain("OPEN_ITEM_NOT_FOUND"));
        });
    }

    /// <summary>Reflects the public string constants declared on <see cref="PaymentErrorCodes"/>.</summary>
    /// <returns>The constant names mapped to their values.</returns>
    private static IReadOnlyDictionary<string, string> DeclaredConstants()
    {
        return typeof(PaymentErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!, StringComparer.Ordinal);
    }
}
