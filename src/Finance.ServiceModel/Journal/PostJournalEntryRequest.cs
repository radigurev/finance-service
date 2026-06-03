namespace Finance.ServiceModel.Journal;

/// <summary>
/// Request body for posting a draft journal entry (SDD-FIN-002 §2.4). Posting requires the current
/// row version for optimistic concurrency; no reason is required (posting is a routine operation).
/// </summary>
public sealed record PostJournalEntryRequest
{
    /// <summary>
    /// Base64-encoded <c>rowversion</c> token captured from the prior read, used for optimistic
    /// concurrency. A stale token is rejected with <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
