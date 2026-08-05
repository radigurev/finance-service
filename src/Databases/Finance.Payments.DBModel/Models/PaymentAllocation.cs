using Finance.GenericFiltering.Attributes;

namespace Finance.Payments.DBModel.Models;

/// <summary>
/// A single sub-ledger MATCH row recording that <see cref="AllocatedAmount"/> of a payment settles a specific
/// invoice (SDD-PAY-002 §2.1). Composed under the <see cref="Payment"/> aggregate and cascade-deleted with it.
/// <para>Allocation is matching, NOT posting: creating or removing a row creates, mutates, and reverses no
/// journal entry, changes no GL or trial-balance figure, and never invokes the payment workflow engine.
/// Because nothing is posted, a row is deliberately MUTABLE and REMOVABLE — a mis-match is corrected by
/// deleting the row and creating the right one, never by a sign-flipped reversal.</para>
/// <para>The identity is <c>INT IDENTITY</c> rather than a sequential GUID because a row is never individually
/// event-exposed: the allocation events carry the <c>(PaymentId, InvoiceId)</c> pair. There is deliberately no
/// status column — a row either exists (matched) or does not (released) — and the settlement state belongs to
/// the invoice open item, not here (§2.8).</para>
/// </summary>
public sealed class PaymentAllocation
{
    /// <summary>Internal <c>INT IDENTITY</c> child identity; never event-exposed and never a sequential GUID.</summary>
    public int Id { get; set; }

    /// <summary>The owning payment (foreign key, cascade-deleted with the aggregate).</summary>
    public Guid PaymentId { get; set; }

    /// <summary>
    /// A CROSS-SERVICE reference to an invoice owned by the <c>finance_invoices</c> database. It is NOT a
    /// foreign key and MUST NEVER be resolved by a cross-database join; existence is asserted against the
    /// local <see cref="InvoiceOpenItem"/> projection.
    /// </summary>
    [Filterable]
    public Guid InvoiceId { get; set; }

    /// <summary>
    /// The transactional amount applied, in the payment's currency (which equals the invoice's currency in
    /// v1). Always strictly positive: zero and negative amounts are rejected.
    /// </summary>
    [Filterable]
    [Sortable]
    public decimal AllocatedAmount { get; set; }

    /// <summary>
    /// The country-rounded base-currency value of <see cref="AllocatedAmount"/> at the payment's frozen rate.
    /// A REPORTING figure only: allocation posts nothing and the ledger stores no rate-converted base amounts,
    /// so this is never reconciled against a journal-entry base amount.
    /// </summary>
    public decimal BaseAllocatedAmount { get; set; }

    /// <summary>
    /// The SIGNED document-level base-currency difference that arises when the payment's frozen exchange rate
    /// differs from the invoice's frozen booking rate (SDD-PAY-002 §2.9). Positive means the payment's own
    /// recorded base value exceeded the invoice's for the allocated portion. Informational only until
    /// SDD-FIN-005 posts it: it changes no GL figure and is never netted into any amount or into the derived
    /// settlement status.
    /// </summary>
    public decimal RealizedFxDifference { get; set; }

    /// <summary>The server timestamp the match was recorded at.</summary>
    [Filterable]
    [Sortable]
    public DateTimeOffset AllocatedAt { get; set; }

    /// <summary>The identifier of the user who recorded the match.</summary>
    public Guid AllocatedBy { get; set; }

    /// <summary>The ambient correlation identifier captured when the match was recorded.</summary>
    public required string CorrelationId { get; set; }

    /// <summary>SQL Server <c>rowversion</c> optimistic-concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The owning payment navigation (the inverse of the aggregate's allocation collection).</summary>
    public Payment? Payment { get; set; }
}
