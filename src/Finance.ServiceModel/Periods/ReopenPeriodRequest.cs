namespace Finance.ServiceModel.Periods;

/// <summary>
/// Request body for reopening a closed fiscal period (SDD-FIN-004 §2.5). A non-empty <see cref="Reason"/>
/// is mandatory (reopen is on the SDD-AUDIT-001 mandatory-reason list).
/// </summary>
public sealed record ReopenPeriodRequest
{
    /// <summary>The mandatory operator-supplied reason for reopening the period.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Base64-encoded <c>rowversion</c> token of the period being reopened, used for optimistic concurrency.
    /// A stale token is rejected with <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
