namespace Finance.ServiceModel.Invoices;

/// <summary>
/// Request body for cancelling (voiding) a draft or confirmed invoice (SDD-INV-001 §2.6). A non-empty
/// <see cref="Reason"/> is mandatory (cancellation voids a numbered document — sensitive).
/// </summary>
public sealed record CancelInvoiceRequest
{
    /// <summary>The mandatory operator-supplied reason for the cancellation.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Base64-encoded <c>rowversion</c> token captured from the prior read, used for optimistic concurrency.
    /// A stale token is rejected with <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
