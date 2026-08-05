namespace Finance.ServiceModel.Payments;

/// <summary>
/// Response body for a successful deallocate call (SDD-PAY-002 §2.6). Mirrors
/// <see cref="AllocatePaymentResultDto"/> so the release path also needs no follow-up read: it carries the
/// removed row's identity and released amount, the payment's new allocated and computed unallocated amounts,
/// its incremented row version, and the affected invoice's post-release settlement state.
/// </summary>
public sealed record DeallocatePaymentResultDto
{
    /// <summary>The payment whose matching changed.</summary>
    public required Guid PaymentId { get; init; }

    /// <summary>The identity of the allocation row that was removed.</summary>
    public required int AllocationId { get; init; }

    /// <summary>The cross-service identifier of the invoice the amount was released from.</summary>
    public required Guid InvoiceId { get; init; }

    /// <summary>The transactional amount released back to the payment's unallocated balance.</summary>
    public required decimal ReleasedAmount { get; init; }

    /// <summary>The payment's total matched amount after the release.</summary>
    public required decimal AllocatedAmount { get; init; }

    /// <summary>The payment's still-unmatched amount after the release — computed, never stored.</summary>
    public required decimal UnallocatedAmount { get; init; }

    /// <summary>The payment's incremented base64 <c>rowversion</c> token for the caller's next write.</summary>
    public required string RowVersion { get; init; }

    /// <summary>The affected invoice's post-release settled amount and derived settlement state.</summary>
    public required AllocatedInvoiceSettlementDto AffectedInvoice { get; init; }
}
