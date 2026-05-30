using System.Reflection;
using System.Runtime.CompilerServices;
using Finance.Infrastructure.Audit.Models;
using Finance.Infrastructure.Stateful.Tests.Audit.Builders;
using NUnit.Framework;

namespace Finance.Infrastructure.Stateful.Tests.Audit;

/// <summary>
/// Unit tests for the <see cref="AuditEntry"/> contract shape (SDD-AUDIT-001 §2.3, §5). The
/// caller-facing record carries the legally-meaningful "who, what, when, why" fields as <c>required</c>
/// init-only members; <see cref="AuditEntry.BeforeJson"/> and <see cref="AuditEntry.Reason"/> are
/// nullable. The compiler enforces the <c>required</c> members; these tests assert the runtime shape.
/// </summary>
[TestFixture]
[Category("SDD-AUDIT-001")]
public sealed class AuditEntryTests
{
    /// <summary>A fully-populated entry exposes every supplied field value.</summary>
    [Test]
    public void AuditEntry_FullyPopulated_ExposesAllFields()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        DateTimeOffset occurredAt = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

        // Act
        AuditEntry entry = new()
        {
            EventType = "InvoiceCancelled",
            Operation = AuditOperation.StateChange,
            EntityType = "Invoice",
            EntityId = "1001",
            UserId = userId,
            Username = "auditor",
            OccurredAt = occurredAt,
            CorrelationId = "corr-123",
            BeforeJson = "{\"status\":\"Confirmed\"}",
            AfterJson = "{\"status\":\"Cancelled\"}",
            Reason = "Duplicate invoice."
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(entry.EventType, Is.EqualTo("InvoiceCancelled"));
            Assert.That(entry.EntityType, Is.EqualTo("Invoice"));
            Assert.That(entry.EntityId, Is.EqualTo("1001"));
            Assert.That(entry.UserId, Is.EqualTo(userId));
            Assert.That(entry.Username, Is.EqualTo("auditor"));
            Assert.That(entry.OccurredAt, Is.EqualTo(occurredAt));
            Assert.That(entry.CorrelationId, Is.EqualTo("corr-123"));
            Assert.That(entry.BeforeJson, Is.EqualTo("{\"status\":\"Confirmed\"}"));
            Assert.That(entry.AfterJson, Is.EqualTo("{\"status\":\"Cancelled\"}"));
            Assert.That(entry.Reason, Is.EqualTo("Duplicate invoice."));
        });
    }

    /// <summary>The "why"/snapshot fields are optional: BeforeJson and Reason default to null on a create entry.</summary>
    [Test]
    public void AuditEntry_CreateEntry_LeavesBeforeJsonAndReasonNull()
    {
        // Arrange & Act
        AuditEntry entry = new AuditEntryBuilder().Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(entry.BeforeJson, Is.Null);
            Assert.That(entry.Reason, Is.Null);
        });
    }

    /// <summary>The legally-meaningful fields carry the <c>required</c> modifier so they cannot be omitted at construction.</summary>
    [Test]
    public void AuditEntry_CoreFields_AreRequiredMembers()
    {
        // Arrange
        string[] coreFields =
        [
            nameof(AuditEntry.EventType),
            nameof(AuditEntry.EntityType),
            nameof(AuditEntry.EntityId),
            nameof(AuditEntry.UserId),
            nameof(AuditEntry.Username),
            nameof(AuditEntry.OccurredAt),
            nameof(AuditEntry.CorrelationId),
            nameof(AuditEntry.AfterJson)
        ];

        // Act
        bool allRequired = coreFields.All(IsRequiredMember);

        // Assert
        Assert.That(allRequired, Is.True);
    }

    private static bool IsRequiredMember(string propertyName)
    {
        PropertyInfo property = typeof(AuditEntry).GetProperty(propertyName)!;
        return property.GetCustomAttribute<RequiredMemberAttribute>() is not null;
    }
}
