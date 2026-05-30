namespace Finance.ServiceModel.Events;

/// <summary>
/// Base marker contract every Finance domain event MUST implement (SDD-INFRA-006 §2.2).
/// Concrete events are <c>sealed record</c> types with <c>required</c> init-only properties that
/// arrive in later batches (Account, Currency, JournalEntry…); this batch ships only the marker.
/// <para>
/// <see cref="CorrelationId"/> MUST be sourced from <c>ICorrelationIdAccessor.Get()</c>,
/// <see cref="MessageId"/> MUST be <c>Guid.NewGuid()</c> at construction, and
/// <see cref="OccurredAt"/> MUST be <c>DateTimeOffset.UtcNow</c>.
/// </para>
/// </summary>
public interface IFinanceEvent
{
    /// <summary>Gets the unique message identifier used by the idempotency filter and outbox de-duplication.</summary>
    Guid MessageId { get; }

    /// <summary>Gets the ambient correlation identifier carried across the service and broker boundaries.</summary>
    string CorrelationId { get; }

    /// <summary>Gets the UTC instant at which the originating domain change occurred.</summary>
    DateTimeOffset OccurredAt { get; }
}
