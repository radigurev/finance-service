using Finance.Common.Enums;

namespace Finance.ServiceModel.Journal;

/// <summary>
/// Representation of a journal entry exposed by the Journal API (SDD-FIN-001 §2.1, SDD-FIN-002).
/// </summary>
public sealed record JournalEntryDto
{
    /// <summary>Sequential-GUID identifier of the entry (event-exposed and externally referenced).</summary>
    public required Guid Id { get; init; }

    /// <summary>The gapless document number assigned at posting; <c>null</c> while <c>Draft</c>.</summary>
    public string? EntryNumber { get; init; }

    /// <summary>The accounting date of the transaction.</summary>
    public required DateTimeOffset EntryDate { get; init; }

    /// <summary>Human-readable memo describing the entry.</summary>
    public required string Description { get; init; }

    /// <summary>The base currency the entry balances in (frozen at creation).</summary>
    public required string BaseCurrencyCode { get; init; }

    /// <summary>The lifecycle state: <c>Draft</c>, <c>Posted</c>, or <c>Reversed</c>.</summary>
    public required JournalEntryStatus Status { get; init; }

    /// <summary>On a reversal entry, the identifier of the original entry it reverses; otherwise <c>null</c>.</summary>
    public Guid? ReversesEntryId { get; init; }

    /// <summary>UTC-offset creation timestamp.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The posting timestamp; <c>null</c> while <c>Draft</c>.</summary>
    public DateTimeOffset? PostedAt { get; init; }

    /// <summary>The lines composing the entry, ordered by <see cref="JournalEntryLineDto.LineNumber"/>.</summary>
    public required IReadOnlyList<JournalEntryLineDto> Lines { get; init; }

    /// <summary>
    /// Base64-encoded SQL Server <c>rowversion</c> optimistic-concurrency token. Clients round-trip
    /// this value back on update so a stale write is rejected with <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
