namespace Finance.ServiceModel.Journal;

/// <summary>
/// Request body for creating a draft journal entry with caller-supplied lines (SDD-FIN-002 §2.3).
/// The base currency is sourced from configuration server-side and is not part of the request.
/// </summary>
public sealed record CreateJournalEntryRequest
{
    /// <summary>The accounting date of the transaction (used for period assignment).</summary>
    public required DateTimeOffset EntryDate { get; init; }

    /// <summary>Human-readable memo describing the entry.</summary>
    public required string Description { get; init; }

    /// <summary>The lines composing the entry (minimum two, balanced in base currency).</summary>
    public required IReadOnlyList<JournalEntryLineRequest> Lines { get; init; }
}
