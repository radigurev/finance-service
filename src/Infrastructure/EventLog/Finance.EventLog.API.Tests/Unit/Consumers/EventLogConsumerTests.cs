using Finance.EventLog.API.Mapping;
using Finance.EventLog.API.Tests.Builders;
using Finance.EventLog.API.Tests.Fixtures;
using Finance.EventLog.DBModel.Models;
using Finance.ServiceModel.Events.Accounts;
using Finance.ServiceModel.Events.Nomenclature;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Finance.EventLog.API.Tests.Unit.Consumers;

/// <summary>
/// Unit tests for the EventLog MassTransit consumers (SDD-EVTLOG-001 §2.1, §2.3) exercised on the
/// in-memory test harness (driven by <c>AddFinanceMessagingTestHarness</c>) with the production idempotency
/// filter over a SETNX-emulating Redis seam. A first delivery persists exactly one archive row; a replay of
/// the same message id is suppressed. Runs fully offline (no RabbitMQ, no Redis, no SQL Server).
/// </summary>
[TestFixture]
[Category("SDD-EVTLOG-001")]
public sealed class EventLogConsumerTests
{
    private SqliteEventLogDbContextScope _scope = null!;
    private EventLogConsumerTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed, started harness before each test.</summary>
    [SetUp]
    public async Task SetUpAsync()
    {
        _scope = SqliteEventLogDbContextFactory.Create();
        _harness = await EventLogConsumerTestHarness.StartAsync(_scope.Context);
    }

    /// <summary>Disposes the harness and SQLite scope after each test.</summary>
    [TearDown]
    public async Task TearDownAsync()
    {
        await _harness.DisposeAsync();
        _scope.Dispose();
    }

    /// <summary>A first AccountCreatedEvent delivery persists exactly one archive row (SDD-EVTLOG-001 §2.1).</summary>
    [Test]
    public async Task ConsumeAsync_AccountCreatedEvent_PersistsEventLogEntry()
    {
        // Arrange
        Guid messageId = Guid.NewGuid();
        AccountCreatedEvent message = TestEventBuilder.AccountCreated(messageId, DateTimeOffset.UtcNow);

        // Act
        await _harness.Harness.Bus.Publish(message, context => context.MessageId = messageId);
        Assert.That(await _harness.Harness.Consumed.Any<AccountCreatedEvent>(), Is.True);

        // Assert
        List<EventLogEntry> entries =
            await _scope.Context.EventLogEntries.AsNoTracking().ToListAsync(CancellationToken.None);
        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(entries[0].EventId, Is.EqualTo(messageId));
            Assert.That(entries[0].EventType, Is.EqualTo(nameof(AccountCreatedEvent)));
            Assert.That(entries[0].SourceService, Is.EqualTo(EventLogSourceServices.Accounts));
            Assert.That(entries[0].CorrelationId, Is.EqualTo(TestEventBuilder.CorrelationId));
        });
    }

    /// <summary>A first CurrencyCreatedEvent delivery persists exactly one archive row (SDD-EVTLOG-001 §2.1).</summary>
    [Test]
    public async Task ConsumeAsync_CurrencyCreatedEvent_PersistsEventLogEntry()
    {
        // Arrange
        Guid messageId = Guid.NewGuid();
        CurrencyCreatedEvent message = TestEventBuilder.CurrencyCreated(messageId, DateTimeOffset.UtcNow);

        // Act
        await _harness.Harness.Bus.Publish(message, context => context.MessageId = messageId);
        Assert.That(await _harness.Harness.Consumed.Any<CurrencyCreatedEvent>(), Is.True);

        // Assert
        EventLogEntry? entry = await _scope.Context.EventLogEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry!.EventId, Is.EqualTo(messageId));
            Assert.That(entry.EventType, Is.EqualTo(nameof(CurrencyCreatedEvent)));
            Assert.That(entry.SourceService, Is.EqualTo(EventLogSourceServices.Nomenclature));
        });
    }

    /// <summary>A replayed message id is suppressed by the idempotency filter (SDD-EVTLOG-001 §2.3).</summary>
    [Test]
    public async Task ConsumeAsync_ReplayedMessageId_DoesNotDuplicateEntry()
    {
        // Arrange
        Guid messageId = Guid.NewGuid();
        AccountCreatedEvent message = TestEventBuilder.AccountCreated(messageId, DateTimeOffset.UtcNow);

        // Act
        await _harness.Harness.Bus.Publish(message, context => context.MessageId = messageId);
        Assert.That(await _harness.Harness.Consumed.Any<AccountCreatedEvent>(), Is.True);

        await _harness.Harness.Bus.Publish(message, context => context.MessageId = messageId);
        await _harness.Harness.InactivityTask;

        // Assert
        int rowCount = await _scope.Context.EventLogEntries.AsNoTracking().CountAsync(CancellationToken.None);
        Assert.That(rowCount, Is.EqualTo(1));
    }
}
