using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Journal.API.Tests.Fixtures;
using Finance.Journal.API.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit.Workflow;

/// <summary>
/// Unit tests for <see cref="GatewayPostingPeriodGuard"/> (SDD-FIN-004 §2.7, §6.4; SDD-FIN-002 §2.7). The
/// guard fulfills the dormant Batch-10 <c>IPostingPeriodGuard</c> seam by reading period status through the
/// Periods <c>by-date</c> lookup. It maps an open period to success and collapses closed, no-period-for-date
/// (404), and unreachable / upstream-error outcomes to <c>POSTING_PERIOD_CLOSED</c> (fail-closed). The
/// Periods read is faked by <see cref="FakePeriodReadClient"/> so the tests run offline.
/// </summary>
[TestFixture]
[Category("SDD-FIN-004")]
[Category("SDD-FIN-002")]
public sealed class GatewayPostingPeriodGuardTests
{
    private FakePeriodReadClient _periods = null!;
    private GatewayPostingPeriodGuard _sut = null!;

    /// <summary>Creates a fresh faked Periods reader and guard before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _periods = new FakePeriodReadClient();
        _sut = new GatewayPostingPeriodGuard(_periods, NullLogger<GatewayPostingPeriodGuard>.Instance);
    }

    /// <summary>An open period for the entry date allows posting (§2.7, §6.4).</summary>
    [Test]
    public async Task GatewayPostingPeriodGuard_OpenPeriod_ReturnsSuccess()
    {
        // Arrange
        _periods.ReturnsOpenPeriod();

        // Act
        Result result = await _sut.EnsurePostableAsync(
            new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
    }

    /// <summary>A closed period for the entry date blocks posting with POSTING_PERIOD_CLOSED (§2.7, §6.4).</summary>
    [Test]
    public async Task GatewayPostingPeriodGuard_ClosedPeriod_ReturnsPostingPeriodClosed()
    {
        // Arrange
        _periods.ReturnsClosedPeriod();

        // Act
        Result result = await _sut.EnsurePostableAsync(
            new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.POSTING_PERIOD_CLOSED));
        });
    }

    /// <summary>A 404 NO_PERIOD_FOR_DATE collapses to POSTING_PERIOD_CLOSED (§2.7, §6.4).</summary>
    [Test]
    public async Task GatewayPostingPeriodGuard_NoPeriodForDate_ReturnsPostingPeriodClosed()
    {
        // Arrange
        _periods.ReturnsNoPeriodForDate();

        // Act
        Result result = await _sut.EnsurePostableAsync(
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.POSTING_PERIOD_CLOSED));
        });
    }

    /// <summary>An unreachable Periods service fails closed with POSTING_PERIOD_CLOSED (§2.7, §6.4).</summary>
    [Test]
    public async Task GatewayPostingPeriodGuard_PeriodsServiceUnreachable_FailsClosed_ReturnsPostingPeriodClosed()
    {
        // Arrange
        _periods.ThrowsServiceUnreachable();

        // Act
        Result result = await _sut.EnsurePostableAsync(
            new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.POSTING_PERIOD_CLOSED));
        });
    }
}
