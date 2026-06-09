namespace Finance.ServiceModel.Invoices;

/// <summary>
/// Request body for confirming a draft invoice (SDD-INV-001 §2.4). Confirm requires the current row version
/// for optimistic concurrency; no reason is required (issuance is a routine operation).
/// </summary>
public sealed record ConfirmInvoiceRequest
{
    /// <summary>
    /// Base64-encoded <c>rowversion</c> token captured from the prior read, used for optimistic concurrency.
    /// A stale token is rejected with <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
