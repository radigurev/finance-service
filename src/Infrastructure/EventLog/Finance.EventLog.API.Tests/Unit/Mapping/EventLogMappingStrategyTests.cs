using System.Text.Json;
using Finance.EventLog.API.Interfaces;
using Finance.EventLog.API.Mapping;
using Finance.EventLog.API.Tests.Builders;
using Finance.EventLog.DBModel.Models;
using Finance.ServiceModel.Events;
using Finance.ServiceModel.Events.Accounts;
using Finance.ServiceModel.Events.Nomenclature;
using NUnit.Framework;

namespace Finance.EventLog.API.Tests.Unit.Mapping;

/// <summary>
/// Unit tests for the per-event <see cref="EventLogMappingStrategyBase{TEvent}"/> implementations
/// (SDD-EVTLOG-001 §2.2): each strategy maps its event to a correct <c>EventLogEntry</c> (EventType,
/// SourceService, CorrelationId, OccurredAt, ReceivedAt, EventId from the message id, and PayloadJson),
/// and the tolerant serializer options ignore unknown JSON members so Warehouse / event schema evolution
/// does not break consumption. Runs fully offline.
/// </summary>
[TestFixture]
[Category("SDD-EVTLOG-001")]
public sealed class EventLogMappingStrategyTests
{
    private AccountCreatedEventMappingStrategy _accountCreatedStrategy = null!;

    /// <summary>Creates a fresh strategy before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _accountCreatedStrategy = new AccountCreatedEventMappingStrategy();
    }

    /// <summary>The mapped entry takes its <c>EventId</c> from the transport message id (SDD-EVTLOG-001 §2.2).</summary>
    [Test]
    public void MapToEntry_KnownEvent_SetsEventIdFromMessageId()
    {
        // Arrange
        Guid messageId = Guid.NewGuid();
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        DateTimeOffset receivedAt = DateTimeOffset.UtcNow;
        AccountCreatedEvent message = TestEventBuilder.AccountCreated(messageId, occurredAt);

        // Act
        EventLogEntry entry = _accountCreatedStrategy.MapToEntry(message, messageId, receivedAt);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(entry.EventId, Is.EqualTo(messageId));
            Assert.That(entry.EventType, Is.EqualTo(nameof(AccountCreatedEvent)));
            Assert.That(entry.SourceService, Is.EqualTo(EventLogSourceServices.Accounts));
            Assert.That(entry.OccurredAt, Is.EqualTo(occurredAt));
            Assert.That(entry.ReceivedAt, Is.EqualTo(receivedAt));
            Assert.That(entry.CorrelationId, Is.EqualTo(TestEventBuilder.CorrelationId));
            Assert.That(entry.PayloadJson, Does.Contain("\"accountId\":1042"));
        });
    }

    /// <summary>
    /// Each of the six account strategies maps its event with the expected <c>EventType</c> /
    /// <c>SourceService</c> and a non-empty payload (SDD-EVTLOG-001 §2.2).
    /// </summary>
    [Test]
    public void MapToEntry_AccountCreatedStrategy_SetsAccountsSourceAndPayload()
    {
        // Arrange
        Guid messageId = Guid.NewGuid();
        AccountCreatedEvent message = TestEventBuilder.AccountCreated(messageId, DateTimeOffset.UtcNow);
        IEventMappingStrategy<AccountCreatedEvent> strategy = new AccountCreatedEventMappingStrategy();

        // Act
        EventLogEntry entry = strategy.MapToEntry(message, messageId, DateTimeOffset.UtcNow);

        // Assert
        AssertMapped(entry, message, nameof(AccountCreatedEvent), EventLogSourceServices.Accounts, messageId);
    }

    /// <summary>The account-updated strategy maps to the Accounts source (SDD-EVTLOG-001 §2.2).</summary>
    [Test]
    public void MapToEntry_AccountUpdatedStrategy_SetsAccountsSourceAndPayload()
    {
        // Arrange
        Guid messageId = Guid.NewGuid();
        AccountUpdatedEvent message = TestEventBuilder.AccountUpdated(messageId, DateTimeOffset.UtcNow);
        IEventMappingStrategy<AccountUpdatedEvent> strategy = new AccountUpdatedEventMappingStrategy();

        // Act
        EventLogEntry entry = strategy.MapToEntry(message, messageId, DateTimeOffset.UtcNow);

        // Assert
        AssertMapped(entry, message, nameof(AccountUpdatedEvent), EventLogSourceServices.Accounts, messageId);
    }

    /// <summary>The account-deactivated strategy maps to the Accounts source (SDD-EVTLOG-001 §2.2).</summary>
    [Test]
    public void MapToEntry_AccountDeactivatedStrategy_SetsAccountsSourceAndPayload()
    {
        // Arrange
        Guid messageId = Guid.NewGuid();
        AccountDeactivatedEvent message = TestEventBuilder.AccountDeactivated(messageId, DateTimeOffset.UtcNow);
        IEventMappingStrategy<AccountDeactivatedEvent> strategy = new AccountDeactivatedEventMappingStrategy();

        // Act
        EventLogEntry entry = strategy.MapToEntry(message, messageId, DateTimeOffset.UtcNow);

        // Assert
        AssertMapped(entry, message, nameof(AccountDeactivatedEvent), EventLogSourceServices.Accounts, messageId);
    }

    /// <summary>The currency-created strategy maps to the Nomenclature source (SDD-EVTLOG-001 §2.2).</summary>
    [Test]
    public void MapToEntry_CurrencyCreatedStrategy_SetsNomenclatureSourceAndPayload()
    {
        // Arrange
        Guid messageId = Guid.NewGuid();
        CurrencyCreatedEvent message = TestEventBuilder.CurrencyCreated(messageId, DateTimeOffset.UtcNow);
        IEventMappingStrategy<CurrencyCreatedEvent> strategy = new CurrencyCreatedEventMappingStrategy();

        // Act
        EventLogEntry entry = strategy.MapToEntry(message, messageId, DateTimeOffset.UtcNow);

        // Assert
        AssertMapped(entry, message, nameof(CurrencyCreatedEvent), EventLogSourceServices.Nomenclature, messageId);
    }

    /// <summary>The currency-updated strategy maps to the Nomenclature source (SDD-EVTLOG-001 §2.2).</summary>
    [Test]
    public void MapToEntry_CurrencyUpdatedStrategy_SetsNomenclatureSourceAndPayload()
    {
        // Arrange
        Guid messageId = Guid.NewGuid();
        CurrencyUpdatedEvent message = TestEventBuilder.CurrencyUpdated(messageId, DateTimeOffset.UtcNow);
        IEventMappingStrategy<CurrencyUpdatedEvent> strategy = new CurrencyUpdatedEventMappingStrategy();

        // Act
        EventLogEntry entry = strategy.MapToEntry(message, messageId, DateTimeOffset.UtcNow);

        // Assert
        AssertMapped(entry, message, nameof(CurrencyUpdatedEvent), EventLogSourceServices.Nomenclature, messageId);
    }

    /// <summary>The currency-deactivated strategy maps to the Nomenclature source (SDD-EVTLOG-001 §2.2).</summary>
    [Test]
    public void MapToEntry_CurrencyDeactivatedStrategy_SetsNomenclatureSourceAndPayload()
    {
        // Arrange
        Guid messageId = Guid.NewGuid();
        CurrencyDeactivatedEvent message = TestEventBuilder.CurrencyDeactivated(messageId, DateTimeOffset.UtcNow);
        IEventMappingStrategy<CurrencyDeactivatedEvent> strategy = new CurrencyDeactivatedEventMappingStrategy();

        // Act
        EventLogEntry entry = strategy.MapToEntry(message, messageId, DateTimeOffset.UtcNow);

        // Assert
        AssertMapped(entry, message, nameof(CurrencyDeactivatedEvent), EventLogSourceServices.Nomenclature, messageId);
    }

    /// <summary>A null message is rejected before any mapping work runs (SDD-EVTLOG-001 §2.2).</summary>
    [Test]
    public void MapToEntry_NullMessage_ThrowsArgumentNullException()
    {
        // Arrange
        AccountCreatedEventMappingStrategy strategy = new();

        // Act + Assert
        Assert.That(
            () => strategy.MapToEntry(null!, Guid.NewGuid(), DateTimeOffset.UtcNow),
            Throws.TypeOf<ArgumentNullException>());
    }

    /// <summary>
    /// A payload that carries an unknown property still deserializes back to the event without error,
    /// proving the tolerant serializer options (SDD-EVTLOG-001 §2.2).
    /// </summary>
    [Test]
    public void MapToEntry_PayloadWithUnknownProperty_DeserializesWithoutError()
    {
        // Arrange
        Guid messageId = Guid.NewGuid();
        AccountCreatedEvent message = TestEventBuilder.AccountCreated(messageId, DateTimeOffset.UtcNow);
        EventLogEntry entry = _accountCreatedStrategy.MapToEntry(message, messageId, DateTimeOffset.UtcNow);

        string evolvedPayload = entry.PayloadJson.TrimEnd('}')
            + ",\"unknownFutureProperty\":\"value-from-a-newer-schema\"}";

        // Act
        AccountCreatedEvent? roundTripped = JsonSerializer.Deserialize<AccountCreatedEvent>(
            evolvedPayload,
            EventLogJsonOptions.Default);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(roundTripped, Is.Not.Null);
            Assert.That(roundTripped!.AccountId, Is.EqualTo(message.AccountId));
            Assert.That(roundTripped.CorrelationId, Is.EqualTo(message.CorrelationId));
        });
    }

    /// <summary>
    /// Verifies the common mapping invariants shared by every strategy: the entry carries the message id,
    /// the expected event-type / source-service descriptors, the event's correlation id, and a payload.
    /// </summary>
    /// <param name="entry">The mapped archive row under test.</param>
    /// <param name="source">The originating event used to build the entry.</param>
    /// <param name="expectedEventType">The expected <c>EventType</c> descriptor.</param>
    /// <param name="expectedSource">The expected <c>SourceService</c> descriptor.</param>
    /// <param name="messageId">The transport message id the <c>EventId</c> must equal.</param>
    private static void AssertMapped(
        EventLogEntry entry,
        IFinanceEvent source,
        string expectedEventType,
        string expectedSource,
        Guid messageId)
    {
        Assert.Multiple(() =>
        {
            Assert.That(entry.EventId, Is.EqualTo(messageId));
            Assert.That(entry.EventType, Is.EqualTo(expectedEventType));
            Assert.That(entry.SourceService, Is.EqualTo(expectedSource));
            Assert.That(entry.OccurredAt, Is.EqualTo(source.OccurredAt));
            Assert.That(entry.CorrelationId, Is.EqualTo(source.CorrelationId));
            Assert.That(entry.PayloadJson, Is.Not.Empty);
        });
    }
}
