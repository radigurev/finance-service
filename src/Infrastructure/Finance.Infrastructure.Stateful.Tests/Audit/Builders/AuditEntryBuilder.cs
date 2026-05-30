using Finance.Infrastructure.Audit.Models;

namespace Finance.Infrastructure.Stateful.Tests.Audit.Builders;

/// <summary>
/// Fluent builder for <see cref="AuditEntry"/> test data (SDD-AUDIT-001 §2.3). Defaults produce a
/// valid non-sensitive create entry (<see cref="AuditEntry.BeforeJson"/> null); each <c>With*</c>
/// method overrides a single field for the scenario under test.
/// </summary>
public sealed class AuditEntryBuilder
{
    private string _eventType = "AccountCreated";
    private AuditOperation _operation = AuditOperation.Create;
    private string _entityType = "Account";
    private string _entityId = "42";
    private Guid _userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private string _username = "tester";
    private DateTimeOffset _occurredAt = new(2026, 5, 30, 9, 0, 0, TimeSpan.Zero);
    private string _correlationId = "corr-001";
    private string? _beforeJson;
    private string _afterJson = "{\"name\":\"Cash\"}";
    private string? _reason;

    /// <summary>Sets the <see cref="AuditEntry.EventType"/>.</summary>
    /// <param name="eventType">The event type value.</param>
    /// <returns>The same builder for chaining.</returns>
    public AuditEntryBuilder WithEventType(string eventType)
    {
        _eventType = eventType;
        return this;
    }

    /// <summary>Sets the <see cref="AuditEntry.Operation"/> kind of change.</summary>
    /// <param name="operation">The audit operation.</param>
    /// <returns>The same builder for chaining.</returns>
    public AuditEntryBuilder WithOperation(AuditOperation operation)
    {
        _operation = operation;
        return this;
    }

    /// <summary>Sets the <see cref="AuditEntry.BeforeJson"/> pre-change snapshot.</summary>
    /// <param name="beforeJson">The pre-change snapshot, or <c>null</c> for create events.</param>
    /// <returns>The same builder for chaining.</returns>
    public AuditEntryBuilder WithBeforeJson(string? beforeJson)
    {
        _beforeJson = beforeJson;
        return this;
    }

    /// <summary>Sets the operator-supplied <see cref="AuditEntry.Reason"/>.</summary>
    /// <param name="reason">The reason value, or <c>null</c> to omit it.</param>
    /// <returns>The same builder for chaining.</returns>
    public AuditEntryBuilder WithReason(string? reason)
    {
        _reason = reason;
        return this;
    }

    /// <summary>Materializes the configured <see cref="AuditEntry"/>.</summary>
    /// <returns>The built audit entry.</returns>
    public AuditEntry Build() => new()
    {
        EventType = _eventType,
        Operation = _operation,
        EntityType = _entityType,
        EntityId = _entityId,
        UserId = _userId,
        Username = _username,
        OccurredAt = _occurredAt,
        CorrelationId = _correlationId,
        BeforeJson = _beforeJson,
        AfterJson = _afterJson,
        Reason = _reason,
    };
}
