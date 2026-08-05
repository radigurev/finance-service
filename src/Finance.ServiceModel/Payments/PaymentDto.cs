using Finance.Common.Enums;

namespace Finance.ServiceModel.Payments;

/// <summary>
/// Representation of a payment exposed by the Payments API (SDD-PAY-001 §2.11). A single aggregate
/// represents both cash movements, discriminated by <see cref="DocumentType"/> and the derived
/// <see cref="Direction"/>. <c>CreatedBy</c>, <c>ConfirmedBy</c>, and <c>CorrelationId</c> are deliberately
/// NOT exposed (mirroring <c>InvoiceDto</c>).
/// </summary>
public sealed record PaymentDto
{
    /// <summary>Sequential-GUID identifier of the payment (event-exposed and externally referenced).</summary>
    public required Guid Id { get; init; }

    /// <summary>The gapless country-formatted document number assigned at confirm; <c>null</c> while <c>Draft</c>.</summary>
    public string? DocumentNumber { get; init; }

    /// <summary>The document type discriminating the two cash documents.</summary>
    public required PaymentDocumentType DocumentType { get; init; }

    /// <summary>The ledger direction (<c>AP</c>/<c>AR</c>) derived from <see cref="DocumentType"/>.</summary>
    public required PaymentDirection Direction { get; init; }

    /// <summary>How the cash moved (<c>Cash</c>/<c>BankTransfer</c>/<c>Card</c>).</summary>
    public required PaymentMethod Method { get; init; }

    /// <summary>The lifecycle state: <c>Draft</c>, <c>Confirmed</c>, <c>Posted</c>, <c>Cancelled</c>, or <c>Reversed</c>.</summary>
    public required PaymentStatus Status { get; init; }

    /// <summary>The Warehouse-owned counterparty (customer/supplier) reference (no cross-service join).</summary>
    public required Guid CounterpartyId { get; init; }

    /// <summary>ISO 4217 alphabetic transactional currency code.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The base currency the payment reports in (frozen at creation from the country strategy).</summary>
    public required string BaseCurrencyCode { get; init; }

    /// <summary>The transactional cash amount (always strictly positive).</summary>
    public required decimal Amount { get; init; }

    /// <summary>The rate at <see cref="PaymentDate"/>; exactly <c>1.000000</c> for a base-currency payment.</summary>
    public required decimal ExchangeRate { get; init; }

    /// <summary>The base-currency value of <see cref="Amount"/>, rounded by the country strategy.</summary>
    public required decimal BaseAmount { get; init; }

    /// <summary>The cash/bank GL account the movement is recorded against (SDD-ACCT-001).</summary>
    public required int SettlementAccountId { get; init; }

    /// <summary>The date the cash moved (drives period assignment and the numbering year guard).</summary>
    public required DateTimeOffset PaymentDate { get; init; }

    /// <summary>Optional operator-supplied bank/transaction reference.</summary>
    public string? BankReference { get; init; }

    /// <summary>The amount already matched against invoices; maintained exclusively by SDD-PAY-002.</summary>
    public required decimal AllocatedAmount { get; init; }

    /// <summary>The still-unmatched amount (<c>Amount − AllocatedAmount</c>), computed and never stored.</summary>
    public required decimal UnallocatedAmount { get; init; }

    /// <summary>The linked journal entry once posted; <c>null</c> until the posting handshake completes.</summary>
    public Guid? JournalEntryId { get; init; }

    /// <summary>The operator-supplied cancellation reason; <c>null</c> unless the draft was cancelled.</summary>
    public string? CancellationReason { get; init; }

    /// <summary>UTC-offset creation timestamp.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The confirm timestamp; <c>null</c> while <c>Draft</c>.</summary>
    public DateTimeOffset? ConfirmedAt { get; init; }

    /// <summary>The posting timestamp; <c>null</c> until <c>Posted</c>.</summary>
    public DateTimeOffset? PostedAt { get; init; }

    /// <summary>The reversal timestamp; <c>null</c> until <c>Reversed</c>.</summary>
    public DateTimeOffset? ReversedAt { get; init; }

    /// <summary>
    /// Base64-encoded SQL Server <c>rowversion</c> optimistic-concurrency token. Clients round-trip this
    /// value back on update / confirm / post / cancel / reverse so a stale write is rejected with
    /// <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
