using Finance.GenericFiltering.Attributes;

namespace Finance.Payments.DBModel.Models;

/// <summary>
/// A LOCAL, event-fed read projection of an invoice a payment may be matched against (SDD-PAY-002 §2.2). It
/// lives inside <c>finance_payments</c> so allocation (§2.5) and aging (SDD-PAY-003) never cross-join another
/// service's database and never depend on the Invoices service being reachable.
/// <para>The primary key is the MIRRORED external <see cref="InvoiceId"/>: there is no surrogate identity and
/// the value is never generated, because it ALWAYS arrives on the source event. The projection is fed only by
/// the invoice's own immutable domain events, so it is not a business-transaction record of its own and needs
/// neither an event nor an audit row.</para>
/// <para><b>Ownership is split.</b> <see cref="SettledAmount"/> is LOCALLY owned — it equals the sum of this
/// database's allocation rows for the invoice and is maintained in the same transaction as the allocation
/// write; the projection consumers MUST NEVER write it. Every other column is EXTERNALLY owned: only the
/// consumers write them and no endpoint exposes a write path to them. The Invoices service keeps its own
/// independent settled-amount copy from the events this service publishes; the two are never cross-checked
/// synchronously.</para>
/// <para><b>Eventual consistency is explicit.</b> The projection lags its source by the outbox delivery
/// latency, so an invoice confirmed moments ago may not yet be allocatable — a legitimate transient
/// <c>PAYMENT_ALLOCATION_INVOICE_NOT_FOUND</c>, never a reason to reintroduce a synchronous read-through.</para>
/// </summary>
public sealed class InvoiceOpenItem
{
    /// <summary>
    /// The mirrored cross-service invoice identifier and the primary key. Never generated — the value always
    /// arrives on the source event.
    /// </summary>
    public Guid InvoiceId { get; set; }

    /// <summary>The invoice's document number; empty on a cancellation tombstone raised for a draft cancel.</summary>
    [Filterable]
    [Sortable]
    public required string DocumentNumber { get; set; }

    /// <summary>
    /// The invoice document type name. Only types some payment document type can settle are ever projected
    /// (v1 excludes a credit note), so a non-settleable invoice is absent from the allocation and aging
    /// surface entirely. Opted into the filter/sort surface for the SDD-PAY-003 §2.5 open-item list.
    /// </summary>
    [Filterable]
    [Sortable]
    public required string DocumentType { get; set; }

    /// <summary>The invoice's ledger direction name (<c>AR</c>/<c>AP</c>), compared by name against the payment's.</summary>
    [Filterable]
    [Sortable]
    public required string Direction { get; set; }

    /// <summary>The Warehouse-owned counterparty reference; an opaque GUID, never joined.</summary>
    [Filterable]
    public Guid CounterpartyId { get; set; }

    /// <summary>The invoice's transactional currency code; v1 requires it to equal the payment's.</summary>
    [Filterable]
    [Sortable]
    public required string CurrencyCode { get; set; }

    /// <summary>The base currency the invoice books in.</summary>
    public required string BaseCurrencyCode { get; set; }

    /// <summary>The invoice gross total — the upper bound the sum of its allocations may never exceed.</summary>
    public decimal GrossTotal { get; set; }

    /// <summary>
    /// The exchange rate the invoice FROZE at creation. One of the two inputs to the realized-FX difference
    /// (SDD-PAY-002 §2.9); the other is the payment's own frozen rate. Never a journal-entry line rate.
    /// </summary>
    public decimal BookingExchangeRate { get; set; }

    /// <summary>The invoice issue date.</summary>
    [Filterable]
    [Sortable]
    public DateTimeOffset IssueDate { get; set; }

    /// <summary>The invoice payment due date — the aging bucket key (SDD-PAY-003).</summary>
    [Filterable]
    [Sortable]
    public DateTimeOffset DueDate { get; set; }

    /// <summary>
    /// The mirrored invoice lifecycle status. The value set is exactly <c>Confirmed</c>, <c>Posted</c>,
    /// <c>Cancelled</c>, and <c>Reversed</c> — <c>Draft</c> never appears, because a draft publishes no
    /// confirmation and the only draft-originated row is the cancellation tombstone, which enters as
    /// <c>Cancelled</c>. <c>Cancelled</c> and <c>Reversed</c> are TERMINAL here exactly as they are on the
    /// invoice aggregate: no consumer may move a row out of either, and both are rejected by allocation.
    /// </summary>
    [Filterable]
    [Sortable]
    public required string InvoiceStatus { get; set; }

    /// <summary>
    /// The LOCALLY-owned matched total: the sum of this database's allocation rows for the invoice, maintained
    /// in the same transaction as the allocation write. The projection consumers never write it.
    /// </summary>
    public decimal SettledAmount { get; set; }

    /// <summary>
    /// The server timestamp of the last applied SOURCE EVENT. Externally owned like every column other than
    /// <see cref="SettledAmount"/>: only the projection consumers write it, so an allocate or deallocate leaves
    /// it untouched (the row version still moves, which is what serializes concurrent allocations).
    /// </summary>
    public DateTimeOffset LastAppliedAt { get; set; }

    /// <summary>
    /// SQL Server <c>rowversion</c> optimistic-concurrency token. REQUIRED: it is the serialization point for
    /// two different payments allocating against the SAME invoice concurrently, and it is what makes an
    /// ordinary projection write landing mid-allocation surface as a retryable
    /// <c>CONCURRENT_MODIFICATION</c> rather than as lost settlement.
    /// </summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>
    /// The still-open amount (<c>GrossTotal − SettledAmount</c>). COMPUTED on read and never stored — a stored
    /// copy would be a second source of truth. Ignored by EF.
    /// </summary>
    public decimal Outstanding => GrossTotal - SettledAmount;
}
