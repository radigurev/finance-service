namespace Finance.ServiceModel.Payments;

/// <summary>
/// Request body for the operator-driven post completion of a confirmed payment (SDD-PAY-001 §2.5). The
/// endpoint never posts a journal entry itself: it either reports the already-<c>Posted</c> payment, or
/// re-enqueues <c>PaymentConfirmedEvent</c> and answers <c>PAYMENT_POSTING_PENDING</c>. Carries the current
/// row version for optimistic concurrency.
/// </summary>
public sealed record PostPaymentRequest
{
    /// <summary>
    /// Base64-encoded <c>rowversion</c> token captured from the prior read, used for optimistic concurrency.
    /// A stale or malformed token is rejected with <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
