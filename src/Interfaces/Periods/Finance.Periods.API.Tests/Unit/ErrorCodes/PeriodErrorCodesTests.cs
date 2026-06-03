using Finance.Common.ErrorCodes;
using Finance.Periods.API.ErrorMapping;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace Finance.Periods.API.Tests.Unit.ErrorCodes;

/// <summary>
/// Unit tests for <see cref="PeriodErrorCodes"/> and the Periods error-code → HTTP-status mapping
/// (SDD-FIN-004 §4, §6.3). Verifies every documented code is defined and mapped to the right status.
/// </summary>
[TestFixture]
[Category("SDD-FIN-004")]
public sealed class PeriodErrorCodesTests
{
    /// <summary>Every documented Periods error code is defined as a self-named constant (§4, §6.3).</summary>
    [Test]
    public void PeriodErrorCodes_DefinesAllPeriodCodes()
    {
        // Arrange & Act & Assert — each constant must equal its own name (the SCREAMING_SNAKE_CASE title).
        Assert.Multiple(() =>
        {
            Assert.That(PeriodErrorCodes.PERIOD_NOT_FOUND, Is.EqualTo(nameof(PeriodErrorCodes.PERIOD_NOT_FOUND)));
            Assert.That(PeriodErrorCodes.NO_PERIOD_FOR_DATE, Is.EqualTo(nameof(PeriodErrorCodes.NO_PERIOD_FOR_DATE)));
            Assert.That(PeriodErrorCodes.PERIOD_ALREADY_CLOSED, Is.EqualTo(nameof(PeriodErrorCodes.PERIOD_ALREADY_CLOSED)));
            Assert.That(PeriodErrorCodes.PERIOD_ALREADY_OPEN, Is.EqualTo(nameof(PeriodErrorCodes.PERIOD_ALREADY_OPEN)));
            Assert.That(
                PeriodErrorCodes.INVALID_PERIOD_STATE_TRANSITION,
                Is.EqualTo(nameof(PeriodErrorCodes.INVALID_PERIOD_STATE_TRANSITION)));
            Assert.That(
                PeriodErrorCodes.CANNOT_CLOSE_OUT_OF_ORDER,
                Is.EqualTo(nameof(PeriodErrorCodes.CANNOT_CLOSE_OUT_OF_ORDER)));
            Assert.That(PeriodErrorCodes.OVERLAPPING_PERIOD, Is.EqualTo(nameof(PeriodErrorCodes.OVERLAPPING_PERIOD)));
            Assert.That(PeriodErrorCodes.DUPLICATE_PERIOD, Is.EqualTo(nameof(PeriodErrorCodes.DUPLICATE_PERIOD)));
            Assert.That(PeriodErrorCodes.CLOSE_REASON_REQUIRED, Is.EqualTo(nameof(PeriodErrorCodes.CLOSE_REASON_REQUIRED)));
            Assert.That(PeriodErrorCodes.REOPEN_REASON_REQUIRED, Is.EqualTo(nameof(PeriodErrorCodes.REOPEN_REASON_REQUIRED)));
            Assert.That(PeriodErrorCodes.INVALID_PERIOD, Is.EqualTo(nameof(PeriodErrorCodes.INVALID_PERIOD)));
        });
    }

    /// <summary>State / ordering / uniqueness conflict codes map to 409 Conflict (§4, §6.3).</summary>
    [Test]
    public void StatusMap_ConflictCodes_MapTo409()
    {
        // Arrange
        PeriodErrorCodeToStatusMap map = new();

        // Act & Assert
        Assert.Multiple(() =>
        {
            Assert.That(map.MapToStatus(PeriodErrorCodes.PERIOD_ALREADY_CLOSED), Is.EqualTo(StatusCodes.Status409Conflict));
            Assert.That(map.MapToStatus(PeriodErrorCodes.PERIOD_ALREADY_OPEN), Is.EqualTo(StatusCodes.Status409Conflict));
            Assert.That(
                map.MapToStatus(PeriodErrorCodes.INVALID_PERIOD_STATE_TRANSITION),
                Is.EqualTo(StatusCodes.Status409Conflict));
            Assert.That(
                map.MapToStatus(PeriodErrorCodes.CANNOT_CLOSE_OUT_OF_ORDER),
                Is.EqualTo(StatusCodes.Status409Conflict));
            Assert.That(map.MapToStatus(PeriodErrorCodes.OVERLAPPING_PERIOD), Is.EqualTo(StatusCodes.Status409Conflict));
            Assert.That(map.MapToStatus(PeriodErrorCodes.DUPLICATE_PERIOD), Is.EqualTo(StatusCodes.Status409Conflict));
        });
    }

    /// <summary>NO_PERIOD_FOR_DATE maps to 404 Not Found (§4, §6.3).</summary>
    [Test]
    public void StatusMap_NoPeriodForDate_MapsTo404()
    {
        // Arrange
        PeriodErrorCodeToStatusMap map = new();

        // Act & Assert
        Assert.That(
            map.MapToStatus(PeriodErrorCodes.NO_PERIOD_FOR_DATE),
            Is.EqualTo(StatusCodes.Status404NotFound));
    }
}
