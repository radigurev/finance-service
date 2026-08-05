using Finance.Common.ErrorCodes;
using Finance.Payments.API.ErrorMapping;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.ErrorMapping;

/// <summary>
/// Unit tests for <see cref="PaymentErrorCodeToStatusMap"/> (SDD-PAY-001 §4/§6.4, SDD-PAY-002 §4/§6.5). The map is
/// ONE class shared by both payment specs and carries SIXTEEN explicit conflict entries — SDD-PAY-001's eight
/// lifecycle codes plus SDD-PAY-002's eight allocation codes. Everything the specs name as DELIBERATELY ABSENT must
/// still resolve correctly through the delegated default map.
/// </summary>
[TestFixture]
public sealed class PaymentErrorCodeToStatusMapTests
{
    private PaymentErrorCodeToStatusMap _sut = null!;

    /// <summary>Creates a fresh map before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _sut = new PaymentErrorCodeToStatusMap();
    }

    [Test]
    [Category("SDD-PAY-001")]
    public void PaymentErrorCodeToStatusMap_MapsThisSpecsEightConflictCodesTo409_AndDelegatesTheNamedDefaultMapCodes()
    {
        // Arrange
        string[] lifecycleConflicts =
        [
            PaymentErrorCodes.PAYMENT_NOT_DRAFT,
            PaymentErrorCodes.PAYMENT_NOT_CONFIRMED,
            PaymentErrorCodes.PAYMENT_POSTING_PENDING,
            PaymentErrorCodes.PAYMENT_POSTED_IMMUTABLE,
            PaymentErrorCodes.INVALID_PAYMENT_STATE_TRANSITION,
            PaymentErrorCodes.PAYMENT_PERIOD_CLOSED,
            PaymentErrorCodes.PAYMENT_HAS_ALLOCATIONS,
            PaymentErrorCodes.PAYMENT_DATE_YEAR_MISMATCH
        ];

        // Act
        IReadOnlyList<int> conflictStatuses = [.. lifecycleConflicts.Select(_sut.MapToStatus)];

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(lifecycleConflicts, Has.Length.EqualTo(8));
            Assert.That(conflictStatuses, Is.All.EqualTo(StatusCodes.Status409Conflict));
            Assert.That(
                _sut.MapToStatus(PaymentErrorCodes.PAYMENT_DUPLICATE_DOCUMENT_NUMBER),
                Is.EqualTo(StatusCodes.Status409Conflict),
                "the DUPLICATE pattern resolves through the delegated default map");
            Assert.That(
                _sut.MapToStatus(PaymentErrorCodes.PAYMENT_SETTLEMENT_ACCOUNT_INACTIVE),
                Is.EqualTo(StatusCodes.Status409Conflict),
                "the *_INACTIVE suffix resolves through the delegated default map");
            Assert.That(
                _sut.MapToStatus(PaymentErrorCodes.PAYMENT_NOT_FOUND),
                Is.EqualTo(StatusCodes.Status404NotFound));
            Assert.That(
                _sut.MapToStatus(PaymentErrorCodes.PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND),
                Is.EqualTo(StatusCodes.Status404NotFound));
            Assert.That(
                _sut.MapToStatus(CommonErrorCodes.CONCURRENT_MODIFICATION),
                Is.EqualTo(StatusCodes.Status409Conflict),
                "the CONCURRENT_ prefix resolves through the delegated default map");
        });
    }

    [Test]
    [Category("SDD-PAY-002")]
    public void PaymentErrorCodeToStatusMap_MapsThisSpecsEightConflictCodesTo409_AndDelegatesTheRest()
    {
        // Arrange
        string[] allocationConflicts =
        [
            PaymentErrorCodes.PAYMENT_NOT_ALLOCATABLE,
            PaymentErrorCodes.PAYMENT_ALLOCATION_INVOICE_NOT_ELIGIBLE,
            PaymentErrorCodes.PAYMENT_ALLOCATION_EXCEEDS_PAYMENT,
            PaymentErrorCodes.PAYMENT_ALLOCATION_EXCEEDS_OUTSTANDING,
            PaymentErrorCodes.PAYMENT_ALLOCATION_DIRECTION_MISMATCH,
            PaymentErrorCodes.PAYMENT_ALLOCATION_COUNTERPARTY_MISMATCH,
            PaymentErrorCodes.PAYMENT_ALLOCATION_CURRENCY_MISMATCH,
            PaymentErrorCodes.PAYMENT_ALLOCATION_CONTROL_ACCOUNT_MISMATCH
        ];

        // Act
        IReadOnlyList<int> conflictStatuses = [.. allocationConflicts.Select(_sut.MapToStatus)];

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(allocationConflicts, Has.Length.EqualTo(8));
            Assert.That(conflictStatuses, Is.All.EqualTo(StatusCodes.Status409Conflict));
            Assert.That(
                _sut.MapToStatus(PaymentErrorCodes.PAYMENT_ALLOCATION_DUPLICATE),
                Is.EqualTo(StatusCodes.Status409Conflict),
                "the DUPLICATE pattern resolves through the delegated default map with no explicit entry");
            Assert.That(
                _sut.MapToStatus(PaymentErrorCodes.PAYMENT_ALLOCATION_NOT_FOUND),
                Is.EqualTo(StatusCodes.Status404NotFound));
            Assert.That(
                _sut.MapToStatus(PaymentErrorCodes.PAYMENT_ALLOCATION_INVOICE_NOT_FOUND),
                Is.EqualTo(StatusCodes.Status404NotFound));
            Assert.That(
                _sut.MapToStatus(CommonErrorCodes.CONCURRENT_MODIFICATION),
                Is.EqualTo(StatusCodes.Status409Conflict));
            Assert.That(
                _sut.MapToStatus(PaymentErrorCodes.PAYMENT_ALLOCATION_ITEMS_REQUIRED),
                Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(
                _sut.MapToStatus(PaymentErrorCodes.INVALID_PAYMENT_ALLOCATION_AMOUNT),
                Is.EqualTo(StatusCodes.Status400BadRequest));
        });
    }

    [Test]
    [Category("SDD-PAY-003")]
    public void PaymentErrorCodeToStatusMap_AgingCodes_AllResolveTo400()
    {
        // Arrange
        string[] agingCodes =
        [
            PaymentErrorCodes.INVALID_AGING_AS_OF_DATE,
            PaymentErrorCodes.INVALID_AGING_DIRECTION,
            PaymentErrorCodes.INVALID_AGING_BUCKETS,
            PaymentErrorCodes.INVALID_COUNTERPARTY_ID,
            PaymentErrorCodes.INVALID_AGING_CURRENCY
        ];

        // Act
        IReadOnlyList<int> statuses = [.. agingCodes.Select(_sut.MapToStatus)];

        // Assert
        Assert.That(statuses, Is.All.EqualTo(StatusCodes.Status400BadRequest));
    }
}
