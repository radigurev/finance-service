using Finance.Common.Enums;

namespace Finance.ServiceModel.Events.Payments;

/// <summary>
/// Domain event published through the EF transactional outbox when an allocation row is removed and its matched
/// amount released (SDD-PAY-002 §2.6, §2.10). Deallocation is sub-ledger matching, not posting: it creates,
/// mutates, and reverses no journal entry and changes no GL figure — an allocation row is deliberately
/// removable, and a mis-match is corrected by deleting the row rather than by a sign-flipped reversal.
/// <para><b><see cref="OccurredAt"/> is an ORDERING TOKEN.</b> It MUST be stamped INSIDE the deallocation
/// transaction and MUST NOT be re-stamped at outbox dispatch or publish time: a release whose token looked
/// older than the allocation it reverses would be silently DROPPED by the SDD-INV-001 settlement mirror,
/// leaving the invoice permanently over-settled.</para>
/// </summary>
public sealed record PaymentDeallocatedEvent : IFinanceEvent
{
    /// <inheritdoc />
    public required Guid MessageId { get; init; }

    /// <inheritdoc />
    public required string CorrelationId { get; init; }

    /// <inheritdoc />
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The sequential-GUID identifier of the payment whose matching changed.</summary>
    public required Guid PaymentId { get; init; }

    /// <summary>The gapless country-formatted document number of the payment.</summary>
    public required string DocumentNumber { get; init; }

    /// <summary>The cross-service identifier of the invoice the amount was released from.</summary>
    public required Guid InvoiceId { get; init; }

    /// <summary>The transactional amount released back to the payment's unallocated balance.</summary>
    public required decimal ReleasedAmount { get; init; }

    /// <summary>The country-rounded base-currency value of the released amount at the payment's frozen rate.</summary>
    public required decimal BaseReleasedAmount { get; init; }

    /// <summary>The invoice's locally-owned settled amount AFTER the release.</summary>
    public required decimal InvoiceSettledAmount { get; init; }

    /// <summary>The invoice's derived settlement state after the release (SDD-PAY-002 §2.8).</summary>
    public required SettlementStatus InvoiceSettlementStatus { get; init; }

    /// <summary>The server timestamp of the release, stamped inside the deallocation transaction.</summary>
    public required DateTimeOffset DeallocatedAt { get; init; }
}
