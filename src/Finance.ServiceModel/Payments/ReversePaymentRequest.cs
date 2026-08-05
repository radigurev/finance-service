namespace Finance.ServiceModel.Payments;

/// <summary>
/// Request body for reversing a <c>Posted</c> payment (SDD-PAY-001 §2.7) — the immutability-preserving
/// correction. A non-empty <see cref="Reason"/> is mandatory (SENSITIVE audit operation, SDD-AUDIT-001). The
/// GL correction is a sign-flipped journal entry produced by the Journal service; nothing on the payment
/// header is ever mutated.
/// </summary>
public sealed record ReversePaymentRequest
{
    /// <summary>The mandatory operator-supplied reason for the reversal.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Base64-encoded <c>rowversion</c> token captured from the prior read, used for optimistic concurrency.
    /// A stale or malformed token is rejected with <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
