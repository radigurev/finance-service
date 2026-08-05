using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Payments;

namespace Finance.Payments.API.Validation;

/// <summary>
/// The cross-aggregate validation request for the allocation invariants (SDD-PAY-002 §2.5, SDD-INFRA-007). It
/// carries the TRACKED payment aggregate (with its existing allocation rows), the requested match items, and
/// the TRACKED local open-item projection rows for exactly the requested invoice set — loaded in ONE query, not
/// one per item.
/// <para>The chain runs against the SAME tracked context instance and inside the SAME transaction that performs
/// the write, so the amounts it sums are the amounts persisted. The payment's and the open items'
/// <c>rowversion</c> tokens are the final race guards.</para>
/// <para>The DEALLOCATE path builds this context with an EMPTY <see cref="Items"/> list: every rule except the
/// payment-state rule is a per-item assertion, so an empty list exercises the chain for its state rules only —
/// which is exactly what §2.5 requires, and it avoids duplicating the state check in a second place. Releasing
/// an amount can never breach an upper bound, and a match that was legal when created stays releasable even
/// after the invoice reaches a terminal status.</para>
/// </summary>
public sealed record PaymentAllocationValidationContext
{
    /// <summary>The tracked payment aggregate whose matching is changing, with its existing allocation rows.</summary>
    public required Payment Payment { get; init; }

    /// <summary>The requested match items; EMPTY on the deallocate path.</summary>
    public required IReadOnlyList<AllocatePaymentItem> Items { get; init; }

    /// <summary>
    /// The tracked local open-item projection rows for the requested invoice set, keyed by invoice identifier.
    /// An invoice absent from this map is unknown to the projection — either projection lag or a document type
    /// the admission rule never projects.
    /// </summary>
    public required IReadOnlyDictionary<Guid, InvoiceOpenItem> OpenItems { get; init; }
}
