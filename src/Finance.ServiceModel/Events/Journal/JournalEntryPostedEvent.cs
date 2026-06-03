namespace Finance.ServiceModel.Events.Journal;

/// <summary>
/// Domain event published through the transactional outbox when a journal entry is posted
/// (SDD-FIN-002 §2.11, SDD-INFRA-006 §2.2).
/// <para>Implements <see cref="IFinanceEvent"/>: <see cref="CorrelationId"/> is sourced from
/// <c>ICorrelationIdAccessor.Get()</c>, <see cref="MessageId"/> is a new GUID at construction, and
/// <see cref="OccurredAt"/> is <c>DateTimeOffset.UtcNow</c>.</para>
/// </summary>
public sealed record JournalEntryPostedEvent : IFinanceEvent
{
    /// <inheritdoc />
    public required Guid MessageId { get; init; }

    /// <inheritdoc />
    public required string CorrelationId { get; init; }

    /// <inheritdoc />
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The sequential-GUID identifier of the posted entry.</summary>
    public required Guid JournalEntryId { get; init; }

    /// <summary>The gapless document number assigned at posting.</summary>
    public required string EntryNumber { get; init; }

    /// <summary>The accounting date of the transaction.</summary>
    public required DateTimeOffset EntryDate { get; init; }

    /// <summary>The base currency the entry balances in.</summary>
    public required string BaseCurrencyCode { get; init; }

    /// <summary>The posted lines (account, debit/credit, currency, rate, base amounts).</summary>
    public required IReadOnlyList<JournalEntryPostedLine> Lines { get; init; }
}
