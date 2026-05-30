using System.Reflection;
using Finance.Infrastructure.Stateful.Tests.Messaging.Fixtures;
using Finance.ServiceModel.Events;
using NUnit.Framework;

namespace Finance.Infrastructure.Stateful.Tests.Messaging;

/// <summary>
/// Unit tests for the <see cref="IFinanceEvent"/> marker convention (SDD-INFRA-006 §2.2): the marker
/// exposes <see cref="IFinanceEvent.MessageId"/>, <see cref="IFinanceEvent.CorrelationId"/>, and
/// <see cref="IFinanceEvent.OccurredAt"/>; a representative concrete event implements it as a
/// <c>sealed record</c> and surfaces those members.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-006")]
public sealed class FinanceEventConventionTests
{
    /// <summary>The marker interface declares exactly the three convention members.</summary>
    [Test]
    public void IFinanceEvent_Declares_MessageIdCorrelationIdOccurredAt()
    {
        // Arrange & Act
        string[] memberNames = typeof(IFinanceEvent)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        // Assert
        Assert.That(
            memberNames,
            Is.EquivalentTo(new[] { nameof(IFinanceEvent.MessageId), nameof(IFinanceEvent.CorrelationId), nameof(IFinanceEvent.OccurredAt) }));
    }

    /// <summary>The convention members are typed Guid, string, and DateTimeOffset respectively.</summary>
    [Test]
    public void IFinanceEvent_ConventionMembers_HaveExpectedTypes()
    {
        // Arrange & Act
        Type messageId = typeof(IFinanceEvent).GetProperty(nameof(IFinanceEvent.MessageId))!.PropertyType;
        Type correlationId = typeof(IFinanceEvent).GetProperty(nameof(IFinanceEvent.CorrelationId))!.PropertyType;
        Type occurredAt = typeof(IFinanceEvent).GetProperty(nameof(IFinanceEvent.OccurredAt))!.PropertyType;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(messageId, Is.EqualTo(typeof(Guid)));
            Assert.That(correlationId, Is.EqualTo(typeof(string)));
            Assert.That(occurredAt, Is.EqualTo(typeof(DateTimeOffset)));
        });
    }

    /// <summary>A representative concrete event implements the marker and exposes the convention values.</summary>
    [Test]
    public void EventRecord_ImplementsIFinanceEvent_AndExposesConventionMembers()
    {
        // Arrange
        Guid messageId = Guid.NewGuid();
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
        IFinanceEvent sampleEvent = new SampleFinanceEvent
        {
            MessageId = messageId,
            CorrelationId = "corr-99",
            OccurredAt = occurredAt,
            EntryNumber = "JE-2026-000001"
        };

        // Act & Assert
        Assert.Multiple(() =>
        {
            Assert.That(sampleEvent, Is.InstanceOf<IFinanceEvent>());
            Assert.That(sampleEvent.MessageId, Is.EqualTo(messageId));
            Assert.That(sampleEvent.CorrelationId, Is.EqualTo("corr-99"));
            Assert.That(sampleEvent.OccurredAt, Is.EqualTo(occurredAt));
        });
    }

    /// <summary>Concrete events follow the sealed-record convention so consumers can rely on the shape.</summary>
    [Test]
    public void EventRecord_IsSealedRecord()
    {
        // Arrange
        Type eventType = typeof(SampleFinanceEvent);

        // Act
        bool isRecord = eventType
            .GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(eventType.IsSealed, Is.True);
            Assert.That(isRecord, Is.True);
        });
    }
}
