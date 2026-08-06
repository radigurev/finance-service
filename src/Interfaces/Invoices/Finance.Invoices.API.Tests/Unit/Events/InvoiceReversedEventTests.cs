using System.Reflection;
using System.Runtime.CompilerServices;
using Finance.ServiceModel.Events;
using Finance.ServiceModel.Events.Invoices;
using NUnit.Framework;

namespace Finance.Invoices.API.Tests.Unit.Events;

/// <summary>
/// Unit tests pinning the wire shape of the REAL <see cref="InvoiceReversedEvent"/> record (SDD-INV-001 §2.11,
/// §6.7; SDD-INFRA-006 §2.2). <c>FinanceEventConventionTests</c> asserts the convention against a stub, so
/// nothing else verifies that the SHIPPED reversal event actually carries it — a dropped <c>required</c>, a
/// nullable <c>DocumentNumber</c>, or an unsealed record would pass every behavioural test and only surface at
/// the SDD-PAY-002 consumer.
/// </summary>
[TestFixture]
[Category("SDD-INV-001")]
[Category("SDD-INFRA-006")]
public sealed class InvoiceReversedEventTests
{
    [Test]
    public void InvoiceReversedEvent_ImplementsIFinanceEvent_WithRequiredMessageIdCorrelationIdAndOccurredAt()
    {
        // Arrange
        Guid messageId = Guid.NewGuid();
        DateTimeOffset occurredAt = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

        // Act
        IFinanceEvent reversed = new InvoiceReversedEvent
        {
            MessageId = messageId,
            CorrelationId = "reversal-correlation",
            OccurredAt = occurredAt,
            InvoiceId = Guid.NewGuid(),
            DocumentNumber = "SINV-2026-000001",
            CorrectingInvoiceId = Guid.NewGuid(),
            Reason = "Fully offset by credit note"
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(reversed, Is.InstanceOf<IFinanceEvent>());
            Assert.That(reversed.MessageId, Is.EqualTo(messageId));
            Assert.That(reversed.CorrelationId, Is.EqualTo("reversal-correlation"));
            Assert.That(reversed.OccurredAt, Is.EqualTo(occurredAt));
            Assert.That(IsRequired(nameof(InvoiceReversedEvent.MessageId)), Is.True);
            Assert.That(IsRequired(nameof(InvoiceReversedEvent.CorrelationId)), Is.True);
            Assert.That(IsRequired(nameof(InvoiceReversedEvent.OccurredAt)), Is.True);
        });
    }

    [Test]
    public void InvoiceReversedEvent_DeclaresEveryPayloadMemberAsRequired()
    {
        // Arrange
        string[] payloadMembers =
        [
            nameof(InvoiceReversedEvent.InvoiceId),
            nameof(InvoiceReversedEvent.DocumentNumber),
            nameof(InvoiceReversedEvent.CorrectingInvoiceId),
            nameof(InvoiceReversedEvent.Reason)
        ];

        // Act
        IReadOnlyList<string> optional = [.. payloadMembers.Where(member => !IsRequired(member))];

        // Assert
        Assert.That(optional, Is.Empty);
    }

    [Test]
    public void InvoiceReversedEvent_DocumentNumber_IsNonNullable()
    {
        // Arrange
        PropertyInfo documentNumber =
            typeof(InvoiceReversedEvent).GetProperty(nameof(InvoiceReversedEvent.DocumentNumber))!;
        NullabilityInfoContext context = new();

        // Act
        NullabilityInfo nullability = context.Create(documentNumber);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(documentNumber.PropertyType, Is.EqualTo(typeof(string)));
            Assert.That(
                nullability.ReadState,
                Is.EqualTo(NullabilityState.NotNull),
                "Reversed is reachable only from Posted, and every posted invoice was numbered at confirm");
        });
    }

    [Test]
    public void InvoiceReversedEvent_IsSealedRecord()
    {
        // Arrange
        Type eventType = typeof(InvoiceReversedEvent);

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

    /// <summary>Determines whether the named property is declared with the <c>required</c> modifier.</summary>
    /// <param name="propertyName">The property name.</param>
    /// <returns><c>true</c> when the property carries <see cref="RequiredMemberAttribute"/>.</returns>
    private static bool IsRequired(string propertyName) =>
        typeof(InvoiceReversedEvent)
            .GetProperty(propertyName)!
            .IsDefined(typeof(RequiredMemberAttribute), inherit: false);
}
