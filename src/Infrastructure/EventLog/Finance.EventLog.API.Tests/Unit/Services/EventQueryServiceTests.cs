using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.EventLog.API.Services;
using Finance.EventLog.API.Tests.Fixtures;
using Finance.EventLog.DBModel.Models;
using Finance.GenericFiltering.Models;
using Finance.ServiceModel.EventLog;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Finance.EventLog.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="EventQueryService"/> covering the default <c>OccurredAt</c>-descending order,
/// event-type and correlation-id filtering, the page-size cap, and date-range validation
/// (SDD-EVTLOG-001 §2.4-§2.5, §3). Runs fully offline against a SQLite in-memory context.
/// </summary>
[TestFixture]
[Category("SDD-EVTLOG-001")]
public sealed class EventQueryServiceTests
{
    private SqliteEventLogDbContextScope _scope = null!;
    private EventQueryService _service = null!;

    /// <summary>Creates a fresh SQLite-backed query service before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqliteEventLogDbContextFactory.Create();
        _service = EventQueryServiceTestHarness.Build(_scope.Context);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    /// <summary>With no sort supplied, results are ordered newest-first (SDD-EVTLOG-001 §2.4).</summary>
    [Test]
    public async Task SearchAsync_NoSort_OrdersByOccurredAtDescending()
    {
        // Arrange
        DateTimeOffset baseTime = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        await SeedAsync(
            EventQueryServiceTestHarness.Entry("AccountCreatedEvent", "c1", baseTime),
            EventQueryServiceTestHarness.Entry("AccountUpdatedEvent", "c2", baseTime.AddHours(2)),
            EventQueryServiceTestHarness.Entry("CurrencyCreatedEvent", "c3", baseTime.AddHours(1)));

        // Act
        Result<PagedResult<EventLogEntryDto>> result =
            await _service.SearchAsync(new FilterRequest(), null, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(
            result.Value!.Items.Select(item => item.OccurredAt),
            Is.Ordered.Descending);
    }

    /// <summary>Filtering by event type returns only matching rows (SDD-EVTLOG-001 §2.4).</summary>
    [Test]
    public async Task SearchAsync_FilterByEventType_ReturnsOnlyMatchingEntries()
    {
        // Arrange
        DateTimeOffset baseTime = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        await SeedAsync(
            EventQueryServiceTestHarness.Entry("AccountCreatedEvent", "c1", baseTime),
            EventQueryServiceTestHarness.Entry("CurrencyCreatedEvent", "c2", baseTime.AddHours(1)));

        FilterRequest request = new()
        {
            Filters =
            [
                new FilterCriterion { Field = "EventType", Operator = "eq", Value = "AccountCreatedEvent" }
            ]
        };

        // Act
        Result<PagedResult<EventLogEntryDto>> result =
            await _service.SearchAsync(request, null, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value!.Items, Has.Count.EqualTo(1));
            Assert.That(result.Value.Items[0].EventType, Is.EqualTo("AccountCreatedEvent"));
        });
    }

    /// <summary>The correlationId shortcut returns every row in the trace (SDD-EVTLOG-001 §2.5).</summary>
    [Test]
    public async Task SearchAsync_FilterByCorrelationId_ReturnsAllEntriesInTrace()
    {
        // Arrange
        DateTimeOffset baseTime = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        await SeedAsync(
            EventQueryServiceTestHarness.Entry("AccountCreatedEvent", "trace-1", baseTime),
            EventQueryServiceTestHarness.Entry("CurrencyCreatedEvent", "trace-1", baseTime.AddHours(1)),
            EventQueryServiceTestHarness.Entry("AccountUpdatedEvent", "trace-2", baseTime.AddHours(2)));

        // Act
        Result<PagedResult<EventLogEntryDto>> result =
            await _service.SearchAsync(new FilterRequest(), "trace-1", CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value!.Items, Has.Count.EqualTo(2));
            Assert.That(result.Value.Items.All(item => item.CorrelationId == "trace-1"), Is.True);
        });
    }

    /// <summary>A page size over the cap returns PAGE_SIZE_TOO_LARGE (SDD-EVTLOG-001 §3).</summary>
    [Test]
    public async Task SearchAsync_PageSizeOverLimit_ReturnsPageSizeTooLarge()
    {
        // Arrange
        FilterRequest request = new() { PageSize = 500 };

        // Act
        Result<PagedResult<EventLogEntryDto>> result =
            await _service.SearchAsync(request, null, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(FilterErrorCodes.PAGE_SIZE_TOO_LARGE));
        });
    }

    /// <summary>A from-after-to date range returns INVALID_DATE_RANGE (SDD-EVTLOG-001 §3).</summary>
    [Test]
    public async Task ValidateRange_FromAfterTo_ReturnsInvalidDateRange()
    {
        // Arrange
        FilterRequest request = new()
        {
            Filters =
            [
                new FilterCriterion { Field = "OccurredAt", Operator = "gte", Value = "2026-05-10T00:00:00Z" },
                new FilterCriterion { Field = "OccurredAt", Operator = "lte", Value = "2026-05-01T00:00:00Z" }
            ]
        };

        // Act
        Result<PagedResult<EventLogEntryDto>> result =
            await _service.SearchAsync(request, null, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(EventLogErrorCodes.INVALID_DATE_RANGE));
        });
    }

    private async Task SeedAsync(params EventLogEntry[] entries)
    {
        _scope.Context.EventLogEntries.AddRange(entries);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
    }
}
