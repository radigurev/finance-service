using Finance.Common.Enums;

namespace Finance.Invoices.API.Interfaces;

/// <summary>
/// The transport-agnostic settlement update an SDD-PAY-002 allocation event carries into this service
/// (SDD-INV-001 §2.15). Both <c>PaymentAllocatedEvent</c> and <c>PaymentDeallocatedEvent</c> reduce to this
/// shape, so the mirror has exactly one code path and the two consumers stay thin.
/// <para>The settled amount is ABSOLUTE — the authoritative total the Payments service holds for the invoice
/// after the allocation or release, never a delta — which is what makes a post-TTL replay or a dead-letter
/// redelivery incapable of double-counting cash.</para>
/// </summary>
public sealed record InvoiceSettlementUpdate
{
    /// <summary>The invoice whose settlement mirror the event updates.</summary>
    public required Guid InvoiceId { get; init; }

    /// <summary>
    /// The ABSOLUTE settled amount the Payments service holds for the invoice after the change. It is assigned,
    /// never added or subtracted.
    /// </summary>
    public required decimal SettledAmount { get; init; }

    /// <summary>
    /// The settlement state the publishing service derived. The local recomputation is authoritative for this
    /// database; a disagreement is logged as a warning and MUST NOT be resolved by trusting this value.
    /// </summary>
    public required SettlementStatus ReportedStatus { get; init; }

    /// <summary>
    /// The event's <c>OccurredAt</c> — the ORDERING TOKEN, stamped inside the publishing service's allocation
    /// transaction (SDD-PAY-002 §2.10). It is compared against the invoice's <c>LastSettlementAppliedAt</c>: a
    /// strictly older token is dropped, an equal or newer one is applied, and an applied event stamps it.
    /// </summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The originating event's correlation identifier, used for the log scope and the audit row.</summary>
    public required string CorrelationId { get; init; }

    /// <summary>The originating event type name, recorded on the structured logs for traceability.</summary>
    public required string SourceEvent { get; init; }
}
