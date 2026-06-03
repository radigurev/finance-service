namespace Finance.ServiceModel.Journal;

/// <summary>
/// Request body for updating a draft journal entry (SDD-FIN-002 §2.5). Only a <c>Draft</c> entry may be
/// updated; a posted/reversed entry is immutable and yields <c>CANNOT_EDIT_POSTED_ENTRY</c>.
/// </summary>
public sealed record UpdateJournalEntryRequest
{
    /// <summary>The accounting date of the transaction.</summary>
    public required DateTimeOffset EntryDate { get; init; }

    /// <summary>Human-readable memo describing the entry.</summary>
    public required string Description { get; init; }

    /// <summary>The replacement set of lines (minimum two, balanced in base currency).</summary>
    public required IReadOnlyList<JournalEntryLineRequest> Lines { get; init; }

    /// <summary>
    /// Base64-encoded <c>rowversion</c> token captured from the prior read, used for optimistic
    /// concurrency. A stale token is rejected with <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
