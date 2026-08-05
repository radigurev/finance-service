namespace Finance.ServiceModel.Payments;

/// <summary>
/// Response body for a successful allocate call (SDD-PAY-002 §2.4). Carries the created match rows, the
/// payment's new allocated and computed unallocated amounts, its incremented row version, and each affected
/// invoice's settled amount plus derived settlement state — so the caller needs no follow-up read.
/// <para>Allocate answers <b>200, not 201</b>, and emits no <c>Location</c> header: the created rows are a
/// sub-collection of the payment aggregate, not independently addressable resources (SDD-PAY-002 §2.13).</para>
/// </summary>
public sealed record AllocatePaymentResultDto
{
    /// <summary>The payment whose matching changed.</summary>
    public required Guid PaymentId { get; init; }

    /// <summary>The allocation rows created by this call, one per requested item.</summary>
    public required IReadOnlyList<PaymentAllocationDto> Allocations { get; init; }

    /// <summary>The payment's total matched amount after the call.</summary>
    public required decimal AllocatedAmount { get; init; }

    /// <summary>
    /// The payment's still-unmatched amount after the call (<c>Amount − AllocatedAmount</c>) — computed, never
    /// stored (SDD-PAY-001 §2.8).
    /// </summary>
    public required decimal UnallocatedAmount { get; init; }

    /// <summary>The payment's incremented base64 <c>rowversion</c> token for the caller's next write.</summary>
    public required string RowVersion { get; init; }

    /// <summary>The post-change settlement state of every invoice this call touched.</summary>
    public required IReadOnlyList<AllocatedInvoiceSettlementDto> AffectedInvoices { get; init; }
}
