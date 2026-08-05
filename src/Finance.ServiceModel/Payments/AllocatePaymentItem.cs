namespace Finance.ServiceModel.Payments;

/// <summary>
/// A single explicit match request line: apply <see cref="AllocatedAmount"/> of the payment to
/// <see cref="InvoiceId"/> (SDD-PAY-002 §2.4). v1 REQUIRES the explicit list — automatic FIFO /
/// oldest-due-first matching is deferred and MUST NOT be implied by an omitted or empty list.
/// </summary>
public sealed record AllocatePaymentItem
{
    /// <summary>
    /// The cross-service identifier of the invoice to match. Existence is asserted against the LOCAL
    /// <c>InvoiceOpenItem</c> projection, never by a cross-database join or a synchronous read.
    /// </summary>
    public required Guid InvoiceId { get; init; }

    /// <summary>
    /// The transactional amount to apply, in the payment's currency (which must equal the invoice's). MUST be
    /// strictly greater than zero and carry at most two decimal places.
    /// </summary>
    public required decimal AllocatedAmount { get; init; }
}
