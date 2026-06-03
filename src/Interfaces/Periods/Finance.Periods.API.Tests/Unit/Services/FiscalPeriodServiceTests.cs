using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Infrastructure.Audit.Entities;
using Finance.Periods.API.Auditing;
using Finance.Periods.API.Caching;
using Finance.Periods.API.Tests.Builders;
using Finance.Periods.API.Tests.Fixtures;
using Finance.Periods.DBModel.Models;
using Finance.ServiceModel.Events.Periods;
using Finance.ServiceModel.Periods;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Finance.Periods.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="Finance.Periods.API.Services.FiscalPeriodService"/> covering the
/// Open ⇄ Closed lifecycle: close / reopen (state machine, ordering guard, mandatory reason, stamps, audit,
/// outbox events, status history, cache invalidation), and optimistic concurrency (SDD-FIN-004 §6.1, §6.2).
/// Runs fully offline against a SQLite in-memory <see cref="Finance.Periods.DBModel.PeriodsDbContext"/> with
/// the real workflow engine, ordering guard, calendar, and write-path audit service plus a mocked
/// <see cref="MassTransit.IPublishEndpoint"/> and a recording reference cache.
/// </summary>
[TestFixture]
[Category("SDD-FIN-004")]
public sealed class FiscalPeriodServiceTests
{
    private SqlitePeriodsDbContextScope _scope = null!;
    private FiscalPeriodServiceTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqlitePeriodsDbContextFactory.Create();
        _harness = FiscalPeriodServiceTestHarness.Build(_scope.Context);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    // ---- State machine & lifecycle (SDD-FIN-004 §6.1) ----

    /// <summary>Closing the latest open period transitions it to Closed (§2.4, §6.1).</summary>
    [Test]
    public async Task Close_OpenPeriod_TransitionsToClosed()
    {
        // Arrange
        FiscalPeriod period = await SeedAsync(FiscalPeriodBuilder.Create());

        // Act
        Result<FiscalPeriodDto> result = await _harness.Service.CloseAsync(
            period.Id, CloseRequest(period), CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.That(result.Value!.Status, Is.EqualTo(FiscalPeriodStatus.Closed));
    }

    /// <summary>Closing an already-closed period returns PERIOD_ALREADY_CLOSED (§2.14, §6.1).</summary>
    [Test]
    public async Task Close_AlreadyClosedPeriod_ReturnsPeriodAlreadyClosed()
    {
        // Arrange
        FiscalPeriod period = await SeedAsync(
            FiscalPeriodBuilder.Create().WithStatus(FiscalPeriodStatus.Closed));

        // Act
        Result<FiscalPeriodDto> result = await _harness.Service.CloseAsync(
            period.Id, CloseRequest(period), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PeriodErrorCodes.PERIOD_ALREADY_CLOSED));
        });
    }

    /// <summary>Closing without a reason returns CLOSE_REASON_REQUIRED and changes no state (§2.4, §6.1).</summary>
    [Test]
    public async Task Close_WithoutReason_ReturnsCloseReasonRequired_NoStateChange()
    {
        // Arrange
        FiscalPeriod period = await SeedAsync(FiscalPeriodBuilder.Create());
        ClosePeriodRequest request = new() { Reason = "   ", RowVersion = RowVersion(period) };

        // Act
        Result<FiscalPeriodDto> result = await _harness.Service.CloseAsync(
            period.Id, request, CancellationToken.None);
        FiscalPeriod reloaded = await ReloadAsync(period.Id);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PeriodErrorCodes.CLOSE_REASON_REQUIRED));
            Assert.That(reloaded.Status, Is.EqualTo(FiscalPeriodStatus.Open));
            Assert.That(_harness.PublishedEvents, Is.Empty);
        });
    }

    /// <summary>Closing while an earlier period is still open returns CANNOT_CLOSE_OUT_OF_ORDER (§2.4, §6.1).</summary>
    [Test]
    public async Task Close_WhileEarlierPeriodOpen_ReturnsCannotCloseOutOfOrder()
    {
        // Arrange — period 1 stays open, attempt to close period 2.
        await SeedAsync(FiscalPeriodBuilder.Create().WithPeriodNumber(1)
            .WithDates(Month(1).Start, Month(1).End).WithStatus(FiscalPeriodStatus.Open));
        FiscalPeriod second = await SeedAsync(FiscalPeriodBuilder.Create().WithPeriodNumber(2)
            .WithName("February 2026").WithDates(Month(2).Start, Month(2).End));

        // Act
        Result<FiscalPeriodDto> result = await _harness.Service.CloseAsync(
            second.Id, CloseRequest(second), CancellationToken.None);
        FiscalPeriod reloaded = await ReloadAsync(second.Id);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PeriodErrorCodes.CANNOT_CLOSE_OUT_OF_ORDER));
            Assert.That(reloaded.Status, Is.EqualTo(FiscalPeriodStatus.Open));
        });
    }

    /// <summary>Reopening a closed period transitions it back to Open (§2.5, §6.1).</summary>
    [Test]
    public async Task Reopen_ClosedPeriod_TransitionsToOpen()
    {
        // Arrange
        FiscalPeriod period = await SeedAsync(
            FiscalPeriodBuilder.Create().WithStatus(FiscalPeriodStatus.Closed));

        // Act
        Result<FiscalPeriodDto> result = await _harness.Service.ReopenAsync(
            period.Id, ReopenRequest(period), CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.That(result.Value!.Status, Is.EqualTo(FiscalPeriodStatus.Open));
    }

    /// <summary>Reopening an already-open period returns PERIOD_ALREADY_OPEN (§2.14, §6.1).</summary>
    [Test]
    public async Task Reopen_AlreadyOpenPeriod_ReturnsPeriodAlreadyOpen()
    {
        // Arrange
        FiscalPeriod period = await SeedAsync(FiscalPeriodBuilder.Create());

        // Act
        Result<FiscalPeriodDto> result = await _harness.Service.ReopenAsync(
            period.Id, ReopenRequest(period), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PeriodErrorCodes.PERIOD_ALREADY_OPEN));
        });
    }

    /// <summary>Reopening without a reason returns REOPEN_REASON_REQUIRED and changes no state (§2.5, §6.1).</summary>
    [Test]
    public async Task Reopen_WithoutReason_ReturnsReopenReasonRequired_NoStateChange()
    {
        // Arrange
        FiscalPeriod period = await SeedAsync(
            FiscalPeriodBuilder.Create().WithStatus(FiscalPeriodStatus.Closed));
        ReopenPeriodRequest request = new() { Reason = "", RowVersion = RowVersion(period) };

        // Act
        Result<FiscalPeriodDto> result = await _harness.Service.ReopenAsync(
            period.Id, request, CancellationToken.None);
        FiscalPeriod reloaded = await ReloadAsync(period.Id);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PeriodErrorCodes.REOPEN_REASON_REQUIRED));
            Assert.That(reloaded.Status, Is.EqualTo(FiscalPeriodStatus.Closed));
            Assert.That(_harness.PublishedEvents, Is.Empty);
        });
    }

    /// <summary>Reopening while a later period is still closed returns CANNOT_CLOSE_OUT_OF_ORDER (§2.5, §6.1).</summary>
    [Test]
    public async Task Reopen_WhileLaterPeriodClosed_ReturnsCannotCloseOutOfOrder()
    {
        // Arrange — period 1 and 2 are both closed; reopening period 1 while 2 is closed is illegal.
        FiscalPeriod first = await SeedAsync(FiscalPeriodBuilder.Create().WithPeriodNumber(1)
            .WithDates(Month(1).Start, Month(1).End).WithStatus(FiscalPeriodStatus.Closed));
        await SeedAsync(FiscalPeriodBuilder.Create().WithPeriodNumber(2).WithName("February 2026")
            .WithDates(Month(2).Start, Month(2).End).WithStatus(FiscalPeriodStatus.Closed));

        // Act
        Result<FiscalPeriodDto> result = await _harness.Service.ReopenAsync(
            first.Id, ReopenRequest(first), CancellationToken.None);
        FiscalPeriod reloaded = await ReloadAsync(first.Id);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PeriodErrorCodes.CANNOT_CLOSE_OUT_OF_ORDER));
            Assert.That(reloaded.Status, Is.EqualTo(FiscalPeriodStatus.Closed));
        });
    }

    /// <summary>Open allows only Closed and Closed allows only Open (the two-state machine) (§2.1, §6.1).</summary>
    [Test]
    public async Task Workflow_OpenAllowsOnlyClosed_ClosedAllowsOnlyOpen()
    {
        // Arrange — drive the full legal cycle Open → Closed → Open and confirm each leg succeeds.
        FiscalPeriod period = await SeedAsync(FiscalPeriodBuilder.Create());

        // Act
        Result<FiscalPeriodDto> closed = await _harness.Service.CloseAsync(
            period.Id, CloseRequest(period), CancellationToken.None);
        FiscalPeriod afterClose = await ReloadAsync(period.Id);
        Result<FiscalPeriodDto> reopened = await _harness.Service.ReopenAsync(
            afterClose.Id, ReopenRequest(afterClose), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(closed.IsSuccess, Is.True, closed.ErrorCode);
            Assert.That(closed.Value!.Status, Is.EqualTo(FiscalPeriodStatus.Closed));
            Assert.That(reopened.IsSuccess, Is.True, reopened.ErrorCode);
            Assert.That(reopened.Value!.Status, Is.EqualTo(FiscalPeriodStatus.Open));
        });
    }

    /// <summary>Closing with a stale row version yields CONCURRENT_MODIFICATION (§2.12, §6.1).</summary>
    [Test]
    public async Task Close_StaleRowVersion_ReturnsConcurrentModification()
    {
        // Arrange
        FiscalPeriod period = await SeedAsync(FiscalPeriodBuilder.Create());
        string staleButValid = Convert.ToBase64String(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 });
        ClosePeriodRequest request = new() { Reason = "Closing", RowVersion = staleButValid };

        // Act
        Result<FiscalPeriodDto> result = await _harness.Service.CloseAsync(
            period.Id, request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(CommonErrorCodes.CONCURRENT_MODIFICATION));
        });
    }

    // ---- Close / reopen side effects (SDD-FIN-004 §6.2) ----

    /// <summary>Closing stamps ClosedAt and ClosedBy (§2.4, §6.2).</summary>
    [Test]
    public async Task Close_StampsClosedAtAndClosedBy()
    {
        // Arrange
        FiscalPeriod period = await SeedAsync(FiscalPeriodBuilder.Create());

        // Act
        await _harness.Service.CloseAsync(period.Id, CloseRequest(period), CancellationToken.None);
        FiscalPeriod closed = await ReloadAsync(period.Id);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(closed.ClosedAt, Is.Not.Null);
            Assert.That(closed.ClosedBy, Is.EqualTo(StubCurrentUserAccessor.TestUserId));
        });
    }

    /// <summary>Closing records an audit StateChange with the reason before the outbox publish (§2.4, §2.10, §6.2).</summary>
    [Test]
    public async Task Close_RecordsAuditStateChange_WithReason_BeforeOutboxPublish()
    {
        // Arrange
        FiscalPeriod period = await SeedAsync(FiscalPeriodBuilder.Create());

        // Act
        await _harness.Service.CloseAsync(
            period.Id, CloseRequest(period, "Month-end close"), CancellationToken.None);

        // Assert
        OperationsEvent audit = await _scope.Context.OperationsEvents
            .Where(row => row.EventType == PeriodAuditEventTypes.FiscalPeriodClosed)
            .SingleAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(audit.EntityType, Is.EqualTo(PeriodAuditEventTypes.EntityType));
            Assert.That(audit.Reason, Is.EqualTo("Month-end close"));
            Assert.That(audit.BeforeJson, Is.Not.Null);
            Assert.That(audit.AfterJson, Is.Not.Null);
            Assert.That(_harness.AuditRowsTrackedAtPublishTime.Single(), Is.GreaterThanOrEqualTo(1));
        });
    }

    /// <summary>Closing publishes FiscalPeriodClosedEvent keyed by year/number with the correlation id (§2.9, §6.2).</summary>
    [Test]
    public async Task Close_PublishesFiscalPeriodClosedEvent_WithFiscalYearPeriodNumberAndCorrelationId()
    {
        // Arrange
        FiscalPeriod period = await SeedAsync(FiscalPeriodBuilder.Create().WithPeriodNumber(1));

        // Act
        await _harness.Service.CloseAsync(
            period.Id, CloseRequest(period, "Closing reason"), CancellationToken.None);

        // Assert
        FiscalPeriodClosedEvent published = _harness.PublishedEvents.OfType<FiscalPeriodClosedEvent>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(published.FiscalYear, Is.EqualTo(period.FiscalYear));
            Assert.That(published.PeriodNumber, Is.EqualTo(period.PeriodNumber));
            Assert.That(published.Reason, Is.EqualTo("Closing reason"));
            Assert.That(published.CorrelationId, Is.EqualTo(StubCorrelationIdAccessor.CorrelationId));
            Assert.That(published.MessageId, Is.Not.EqualTo(Guid.Empty));
        });
    }

    /// <summary>Closing appends an Open → Closed status-history row (§2.4, §6.2).</summary>
    [Test]
    public async Task Close_AppendsStatusHistoryRow_OpenToClosed()
    {
        // Arrange
        FiscalPeriod period = await SeedAsync(FiscalPeriodBuilder.Create());

        // Act
        await _harness.Service.CloseAsync(period.Id, CloseRequest(period), CancellationToken.None);

        // Assert
        FiscalPeriodStatusHistory history = await _scope.Context.FiscalPeriodStatusHistory
            .Where(row => row.FiscalPeriodId == period.Id)
            .SingleAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(history.FromStatus, Is.EqualTo(nameof(FiscalPeriodStatus.Open)));
            Assert.That(history.ToStatus, Is.EqualTo(nameof(FiscalPeriodStatus.Closed)));
            Assert.That(history.CorrelationId, Is.EqualTo(StubCorrelationIdAccessor.CorrelationId));
        });
    }

    /// <summary>Closing invalidates the bounded finance-periods cache region (§2.8, §6.2).</summary>
    [Test]
    public async Task Close_InvalidatesFinancePeriodsCacheRegion()
    {
        // Arrange
        FiscalPeriod period = await SeedAsync(FiscalPeriodBuilder.Create());

        // Act
        await _harness.Service.CloseAsync(period.Id, CloseRequest(period), CancellationToken.None);

        // Assert
        Assert.That(_harness.Cache.RemovedPatterns, Does.Contain(PeriodCacheKeys.InvalidationPattern));
    }

    /// <summary>Reopening records an audit StateChange with the reason before the outbox publish (§2.5, §2.10, §6.2).</summary>
    [Test]
    public async Task Reopen_RecordsAuditStateChange_WithReason_BeforeOutboxPublish()
    {
        // Arrange
        FiscalPeriod period = await SeedAsync(
            FiscalPeriodBuilder.Create().WithStatus(FiscalPeriodStatus.Closed));

        // Act
        await _harness.Service.ReopenAsync(
            period.Id, ReopenRequest(period, "Correcting a premature close"), CancellationToken.None);

        // Assert
        OperationsEvent audit = await _scope.Context.OperationsEvents
            .Where(row => row.EventType == PeriodAuditEventTypes.FiscalPeriodReopened)
            .SingleAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(audit.Reason, Is.EqualTo("Correcting a premature close"));
            Assert.That(audit.BeforeJson, Is.Not.Null);
            Assert.That(_harness.AuditRowsTrackedAtPublishTime.Single(), Is.GreaterThanOrEqualTo(1));
        });
    }

    /// <summary>Reopening publishes FiscalPeriodReopenedEvent carrying the reason (§2.9, §6.2).</summary>
    [Test]
    public async Task Reopen_PublishesFiscalPeriodReopenedEvent_WithReason()
    {
        // Arrange
        FiscalPeriod period = await SeedAsync(
            FiscalPeriodBuilder.Create().WithStatus(FiscalPeriodStatus.Closed));

        // Act
        await _harness.Service.ReopenAsync(
            period.Id, ReopenRequest(period, "Reopen reason"), CancellationToken.None);

        // Assert
        FiscalPeriodReopenedEvent published =
            _harness.PublishedEvents.OfType<FiscalPeriodReopenedEvent>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(published.FiscalYear, Is.EqualTo(period.FiscalYear));
            Assert.That(published.PeriodNumber, Is.EqualTo(period.PeriodNumber));
            Assert.That(published.Reason, Is.EqualTo("Reopen reason"));
            Assert.That(published.CorrelationId, Is.EqualTo(StubCorrelationIdAccessor.CorrelationId));
        });
    }

    /// <summary>Reopening appends a Closed → Open status-history row (§2.5, §6.2).</summary>
    [Test]
    public async Task Reopen_AppendsStatusHistoryRow_ClosedToOpen()
    {
        // Arrange
        FiscalPeriod period = await SeedAsync(
            FiscalPeriodBuilder.Create().WithStatus(FiscalPeriodStatus.Closed));

        // Act
        await _harness.Service.ReopenAsync(period.Id, ReopenRequest(period), CancellationToken.None);

        // Assert
        FiscalPeriodStatusHistory history = await _scope.Context.FiscalPeriodStatusHistory
            .Where(row => row.FiscalPeriodId == period.Id)
            .SingleAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(history.FromStatus, Is.EqualTo(nameof(FiscalPeriodStatus.Closed)));
            Assert.That(history.ToStatus, Is.EqualTo(nameof(FiscalPeriodStatus.Open)));
        });
    }

    /// <summary>A guard failure on close publishes no event and writes no status-change audit (§2.4, §6.2).</summary>
    [Test]
    public async Task Close_DoesNotPublishEvent_WhenGuardFails()
    {
        // Arrange — period 1 stays open so closing period 2 trips the ordering guard.
        await SeedAsync(FiscalPeriodBuilder.Create().WithPeriodNumber(1)
            .WithDates(Month(1).Start, Month(1).End).WithStatus(FiscalPeriodStatus.Open));
        FiscalPeriod second = await SeedAsync(FiscalPeriodBuilder.Create().WithPeriodNumber(2)
            .WithName("February 2026").WithDates(Month(2).Start, Month(2).End));

        // Act
        Result<FiscalPeriodDto> result = await _harness.Service.CloseAsync(
            second.Id, CloseRequest(second), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PeriodErrorCodes.CANNOT_CLOSE_OUT_OF_ORDER));
            Assert.That(_harness.PublishedEvents, Is.Empty);
        });
    }

    // ---- Helpers ----

    private async Task<FiscalPeriod> SeedAsync(FiscalPeriodBuilder builder)
    {
        FiscalPeriod period = builder.Build();
        _scope.Context.FiscalPeriods.Add(period);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        _scope.Context.ChangeTracker.Clear();
        return await ReloadAsync(period.Id);
    }

    private async Task<FiscalPeriod> ReloadAsync(int id)
    {
        return await _scope.Context.FiscalPeriods
            .AsNoTracking()
            .SingleAsync(period => period.Id == id, CancellationToken.None);
    }

    private static ClosePeriodRequest CloseRequest(FiscalPeriod period, string reason = "Closing the period")
    {
        return new ClosePeriodRequest { Reason = reason, RowVersion = RowVersion(period) };
    }

    private static ReopenPeriodRequest ReopenRequest(FiscalPeriod period, string reason = "Reopening the period")
    {
        return new ReopenPeriodRequest { Reason = reason, RowVersion = RowVersion(period) };
    }

    private static string RowVersion(FiscalPeriod period) => Convert.ToBase64String(period.RowVersion);

    private static (DateTimeOffset Start, DateTimeOffset End) Month(int month)
    {
        DateTimeOffset start = new(2026, month, 1, 0, 0, 0, TimeSpan.Zero);
        int daysInMonth = DateTime.DaysInMonth(2026, month);
        DateTimeOffset end = new DateTimeOffset(2026, month, daysInMonth, 23, 59, 59, TimeSpan.Zero)
            .AddTicks(TimeSpan.TicksPerSecond - 1);
        return (start, end);
    }
}
