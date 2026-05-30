using Finance.Common.Enums;

namespace Finance.ServiceModel.Events.Accounts;

/// <summary>
/// Domain event published through the transactional outbox when a chart-of-accounts account is updated
/// without being deactivated (SDD-ACCT-001 §2.4, §2.8, SDD-INFRA-006 §2.2).
/// <para>Implements <see cref="IFinanceEvent"/>: <see cref="CorrelationId"/> is sourced from
/// <c>ICorrelationIdAccessor.Get()</c>, <see cref="MessageId"/> is a new GUID at construction, and
/// <see cref="OccurredAt"/> is <c>DateTimeOffset.UtcNow</c>.</para>
/// </summary>
public sealed record AccountUpdatedEvent : IFinanceEvent
{
    /// <inheritdoc />
    public required Guid MessageId { get; init; }

    /// <inheritdoc />
    public required string CorrelationId { get; init; }

    /// <inheritdoc />
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Surrogate identifier of the updated account.</summary>
    public required int AccountId { get; init; }

    /// <summary>Country-specific account code (e.g. "304", "401").</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable account name after the update.</summary>
    public required string Name { get; init; }

    /// <summary>Asset, Liability, Equity, Revenue, or Expense.</summary>
    public required AccountType Type { get; init; }

    /// <summary>ISO 3166-1 alpha-2 country code identifying the owning chart.</summary>
    public required string CountryCode { get; init; }

    /// <summary>Whether the account is active and available for posting after the update.</summary>
    public required bool IsActive { get; init; }
}
