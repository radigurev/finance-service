using Finance.Common.Enums;
using Finance.GenericFiltering.Attributes;

namespace Finance.Payments.DBModel.Models;

/// <summary>
/// The payment aggregate root (SDD-PAY-001 §1, §2.3): a single entity representing both cash movements — a
/// customer receipt (money in, AR) and a supplier payment (money out, AP) — discriminated by
/// <see cref="DocumentType"/> with a derived, frozen <see cref="Direction"/>. Event-exposed and externally
/// referenced (by allocations and journal entries), so its identifier is a sequential GUID. Its lifecycle
/// (<c>Draft → Confirmed → Posted</c>, plus <c>Cancelled</c> from <c>Draft</c> only and <c>Reversed</c> from
/// <c>Posted</c>) is owned by SDD-PAY-001. Confirmed and later payments are immutable: the only columns that
/// may ever be written after confirm are <see cref="JournalEntryId"/>/<see cref="PostedAt"/> (the posting
/// link), the <c>Reversed</c> flag + <see cref="ReversedAt"/>, and <see cref="AllocatedAmount"/> (the
/// SDD-PAY-002 carve-out).
/// </summary>
public sealed class Payment
{
    /// <summary>Sequential-GUID identifier (event-exposed, externally referenced).</summary>
    public Guid Id { get; set; }

    /// <summary>The gapless country-formatted document number assigned at confirm; <c>null</c> while <c>Draft</c>.</summary>
    [Filterable]
    [Sortable]
    [Searchable]
    public string? DocumentNumber { get; set; }

    /// <summary>The document type discriminating the two cash documents.</summary>
    [Filterable]
    [Sortable]
    public PaymentDocumentType DocumentType { get; set; }

    /// <summary>The ledger direction (<c>AP</c>/<c>AR</c>), derived from <see cref="DocumentType"/> and frozen.</summary>
    [Filterable]
    [Sortable]
    public PaymentDirection Direction { get; set; }

    /// <summary>How the cash moved (<c>Cash</c>/<c>BankTransfer</c>/<c>Card</c>).</summary>
    [Filterable]
    [Sortable]
    public PaymentMethod Method { get; set; }

    /// <summary>The lifecycle state.</summary>
    [Filterable]
    [Sortable]
    public PaymentStatus Status { get; set; } = PaymentStatus.Draft;

    /// <summary>The Warehouse-owned counterparty (customer/supplier) reference (no cross-database join).</summary>
    [Filterable]
    public Guid CounterpartyId { get; set; }

    /// <summary>ISO 4217 alphabetic transactional currency code.</summary>
    [Filterable]
    [Sortable]
    public required string CurrencyCode { get; set; }

    /// <summary>The base currency the payment reports in (frozen at creation from the country strategy).</summary>
    public required string BaseCurrencyCode { get; set; }

    /// <summary>The transactional cash amount; always strictly positive (direction carries the sign).</summary>
    [Filterable]
    [Sortable]
    public decimal Amount { get; set; }

    /// <summary>The rate at <see cref="PaymentDate"/>; exactly <c>1.000000</c> for a base-currency payment.</summary>
    public decimal ExchangeRate { get; set; }

    /// <summary>
    /// The base-currency value of <see cref="Amount"/>, computed as the country-rounded
    /// <c>Amount × ExchangeRate</c>. Stored for reporting and for the SDD-PAY-002 realized-FX computation —
    /// it is NOT what is posted to the general ledger in v1 (SDD-PAY-001 §2.5, §2.8).
    /// </summary>
    public decimal BaseAmount { get; set; }

    /// <summary>
    /// The amount already matched against invoices. Defaults to <c>0.00</c> and is maintained EXCLUSIVELY by
    /// the SDD-PAY-002 allocate/deallocate paths — no endpoint in SDD-PAY-001 writes it.
    /// </summary>
    public decimal AllocatedAmount { get; set; }

    /// <summary>
    /// The still-unmatched amount (<c>Amount − AllocatedAmount</c>). Computed and ignored by EF — never a
    /// stored or persisted computed column in v1 (SDD-PAY-001 §2.8).
    /// </summary>
    public decimal UnallocatedAmount => Amount - AllocatedAmount;

    /// <summary>The cash/bank GL account the movement is recorded against (SDD-ACCT-001; no foreign key).</summary>
    [Filterable]
    [Sortable]
    public int SettlementAccountId { get; set; }

    /// <summary>The date the cash moved (drives period assignment and the numbering year guard).</summary>
    [Filterable]
    [Sortable]
    public DateTimeOffset PaymentDate { get; set; }

    /// <summary>Optional operator-supplied bank/transaction reference.</summary>
    public string? BankReference { get; set; }

    /// <summary>The linked journal entry once posted; <c>null</c> until the posting handshake completes.</summary>
    public Guid? JournalEntryId { get; set; }

    /// <summary>The operator-supplied cancellation reason; <c>null</c> unless the draft was cancelled.</summary>
    public string? CancellationReason { get; set; }

    /// <summary>The ambient correlation identifier captured at creation.</summary>
    public required string CorrelationId { get; set; }

    /// <summary>UTC-offset creation timestamp.</summary>
    [Filterable]
    [Sortable]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The identifier of the user who created the payment.</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>The confirm timestamp; <c>null</c> while <c>Draft</c>.</summary>
    public DateTimeOffset? ConfirmedAt { get; set; }

    /// <summary>The identifier of the user who confirmed the payment; <c>null</c> while <c>Draft</c>.</summary>
    public Guid? ConfirmedBy { get; set; }

    /// <summary>The posting timestamp; <c>null</c> until <c>Posted</c>.</summary>
    public DateTimeOffset? PostedAt { get; set; }

    /// <summary>The reversal timestamp; <c>null</c> until <c>Reversed</c>.</summary>
    public DateTimeOffset? ReversedAt { get; set; }

    /// <summary>SQL Server <c>rowversion</c> optimistic-concurrency token (SDD-INFRA-008/009).</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The append-only state-transition history for the payment.</summary>
    public ICollection<PaymentStatusHistory> StatusHistory { get; set; } = new List<PaymentStatusHistory>();

    /// <summary>
    /// The sub-ledger match rows owned by this payment as a composition, cascade-deleted with it
    /// (SDD-PAY-002 §2.1). The navigation is deliberately NOT <c>AutoInclude()</c>d: the SDD-PAY-001 payment
    /// list must not fan out into allocation rows on every read.
    /// </summary>
    public ICollection<PaymentAllocation> Allocations { get; set; } = new List<PaymentAllocation>();
}
