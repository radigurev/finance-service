namespace Finance.ServiceModel.Invoices;

/// <summary>
/// Request body for the operator-driven post completion of a confirmed invoice (SDD-INV-001 §2.5). The
/// endpoint confirms the <c>Posted</c> transition once the Journal back-event has linked a journal entry;
/// otherwise it reports posting-pending. Carries the current row version for optimistic concurrency.
/// </summary>
public sealed record PostInvoiceRequest
{
    /// <summary>
    /// Base64-encoded <c>rowversion</c> token captured from the prior read, used for optimistic concurrency.
    /// A stale token is rejected with <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
