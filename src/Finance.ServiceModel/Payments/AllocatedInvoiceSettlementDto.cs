using Finance.Common.Enums;

namespace Finance.ServiceModel.Payments;

/// <summary>
/// The post-change settlement state of one invoice affected by an allocate or deallocate call
/// (SDD-PAY-002 §2.4, §2.6). Returned on the write responses so the caller needs no follow-up read.
/// <para>This is NOT the projection's read shape: the open-item report DTO (with its computed outstanding,
/// days-past-due, and aging bucket) is owned by SDD-PAY-003, and SDD-PAY-002 declares no mirror of it.</para>
/// </summary>
public sealed record AllocatedInvoiceSettlementDto
{
    /// <summary>The cross-service identifier of the affected invoice.</summary>
    public required Guid InvoiceId { get; init; }

    /// <summary>The invoice's locally-owned settled amount after the change.</summary>
    public required decimal SettledAmount { get; init; }

    /// <summary>The invoice's derived settlement state after the change (SDD-PAY-002 §2.8).</summary>
    public required SettlementStatus SettlementStatus { get; init; }
}
