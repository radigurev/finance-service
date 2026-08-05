namespace Finance.ServiceModel.Payments;

/// <summary>
/// Request body for cancelling (voiding) a <c>Draft</c> payment (SDD-PAY-001 §2.6). Cancel is legal from
/// <c>Draft</c> ONLY; a confirmed-or-later payment is corrected by reversal. A non-empty <see cref="Reason"/>
/// is mandatory — cancellation is a SENSITIVE audit operation (SDD-AUDIT-001).
/// </summary>
public sealed record CancelPaymentRequest
{
    /// <summary>The mandatory operator-supplied reason for the cancellation.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Base64-encoded <c>rowversion</c> token captured from the prior read, used for optimistic concurrency.
    /// A stale or malformed token is rejected with <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
