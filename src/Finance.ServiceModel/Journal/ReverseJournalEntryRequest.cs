namespace Finance.ServiceModel.Journal;

/// <summary>
/// Request body for reversing a posted journal entry (SDD-FIN-002 §2.6). A non-empty
/// <see cref="Reason"/> is mandatory (reversal is on the SDD-AUDIT-001 mandatory-reason list).
/// </summary>
public sealed record ReverseJournalEntryRequest
{
    /// <summary>The mandatory operator-supplied reason for the reversal.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Base64-encoded <c>rowversion</c> token of the entry being reversed, used for optimistic
    /// concurrency. A stale token is rejected with <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
