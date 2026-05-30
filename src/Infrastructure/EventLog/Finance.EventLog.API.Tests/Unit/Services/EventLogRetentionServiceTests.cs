using Finance.EventLog.API.Services;
using Finance.EventLog.API.Tests.Fixtures;
using Finance.EventLog.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Finance.EventLog.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="EventLogRetentionService"/> covering the daily purge of expired rows
/// (SDD-EVTLOG-001 §2.7): rows older than <c>EventLog:RetentionDays</c> are deleted, newer rows are kept,
/// and the deleted count (which the hosted job logs via a structured template) is returned. Runs fully
/// offline against a SQLite in-memory context.
/// </summary>
[TestFixture]
[Category("SDD-EVTLOG-001")]
public sealed class EventLogRetentionServiceTests
{
    private SqliteEventLogDbContextScope _scope = null!;

    /// <summary>Creates a fresh SQLite scope before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqliteEventLogDbContextFactory.Create();
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    /// <summary>Rows older than the retention window are deleted, newer rows kept, count returned (SDD-EVTLOG-001 §2.7).</summary>
    [Test]
    public async Task RunAsync_EntriesOlderThanRetentionDays_DeletesAndLogsCount()
    {
        // Arrange
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await SeedAsync(
            EventQueryServiceTestHarness.Entry("AccountCreatedEvent", "c1", now.AddDays(-120)),
            EventQueryServiceTestHarness.Entry("AccountUpdatedEvent", "c2", now.AddDays(-100)),
            EventQueryServiceTestHarness.Entry("CurrencyCreatedEvent", "c3", now.AddDays(-10)));

        EventLogRetentionService service = BuildService(retentionDays: 90);

        // Act
        int deletedCount = await service.PurgeExpiredAsync(CancellationToken.None);

        // Assert
        List<EventLogEntry> remaining =
            await _scope.Context.EventLogEntries.AsNoTracking().ToListAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(deletedCount, Is.EqualTo(2));
            Assert.That(remaining, Has.Count.EqualTo(1));
            Assert.That(remaining[0].CorrelationId, Is.EqualTo("c3"));
        });
    }

    /// <summary>No rows older than the window means nothing is deleted (SDD-EVTLOG-001 §2.7).</summary>
    [Test]
    public async Task RunAsync_AllEntriesWithinRetentionWindow_DeletesNothing()
    {
        // Arrange
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await SeedAsync(
            EventQueryServiceTestHarness.Entry("AccountCreatedEvent", "c1", now.AddDays(-1)),
            EventQueryServiceTestHarness.Entry("CurrencyCreatedEvent", "c2", now.AddDays(-30)));

        EventLogRetentionService service = BuildService(retentionDays: 90);

        // Act
        int deletedCount = await service.PurgeExpiredAsync(CancellationToken.None);

        // Assert
        int remaining = await _scope.Context.EventLogEntries.AsNoTracking().CountAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(deletedCount, Is.EqualTo(0));
            Assert.That(remaining, Is.EqualTo(2));
        });
    }

    private EventLogRetentionService BuildService(int retentionDays)
    {
        IOptions<EventLogRetentionOptions> options =
            Options.Create(new EventLogRetentionOptions { RetentionDays = retentionDays });
        return new EventLogRetentionService(_scope.Context, options);
    }

    private async Task SeedAsync(params EventLogEntry[] entries)
    {
        _scope.Context.EventLogEntries.AddRange(entries);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
    }
}
