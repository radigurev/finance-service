namespace Finance.ServiceModel.Payments;

/// <summary>
/// Request body for confirming a draft payment (SDD-PAY-001 §2.4). Confirm requires the current row version
/// for optimistic concurrency; no reason is required (issuance is a routine operation), so the request needs
/// no FluentValidation validator.
/// </summary>
public sealed record ConfirmPaymentRequest
{
    /// <summary>
    /// Base64-encoded <c>rowversion</c> token captured from the prior read, used for optimistic concurrency.
    /// A stale or malformed token is rejected with <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
