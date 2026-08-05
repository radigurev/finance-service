namespace Finance.ServiceModel.Payments;

/// <summary>
/// Request body for matching a confirmed-or-later payment against an explicit list of invoices
/// (SDD-PAY-002 §2.4). The request is ALL-OR-NOTHING: one transaction over every item, so a single failing
/// invariant writes no allocation row, publishes no event, records no audit row, and leaves the payment's
/// allocated amount unchanged.
/// </summary>
public sealed record AllocatePaymentRequest
{
    /// <summary>
    /// The explicit match lines. An empty list is rejected with <c>PAYMENT_ALLOCATION_ITEMS_REQUIRED</c> — it
    /// is never read as an implicit "apply the whole payment".
    /// </summary>
    public required IReadOnlyList<AllocatePaymentItem> Items { get; init; }

    /// <summary>
    /// Base64-encoded payment <c>rowversion</c> token captured from the prior read. The payment row is the
    /// serialization point for the "sum of allocations never exceeds the payment amount" invariant, so a stale
    /// or malformed token is rejected with <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
