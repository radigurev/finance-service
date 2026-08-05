using Finance.Common.Enums;

namespace Finance.ServiceModel.Payments;

/// <summary>
/// Request body for updating a draft payment (SDD-PAY-001 §2.6). Only a <c>Draft</c> payment may be updated;
/// a confirmed-or-later payment is immutable. <see cref="DocumentType"/> is carried so the service can reject
/// an attempt to change it (it drives the direction, the sequence key, and the posting rule).
/// </summary>
public sealed record UpdatePaymentRequest
{
    /// <summary>The document type, which MUST equal the persisted draft's value.</summary>
    public required PaymentDocumentType DocumentType { get; init; }

    /// <summary>How the cash moved (<c>Cash</c>/<c>BankTransfer</c>/<c>Card</c>).</summary>
    public required PaymentMethod Method { get; init; }

    /// <summary>The Warehouse-owned counterparty (customer/supplier) reference.</summary>
    public required Guid CounterpartyId { get; init; }

    /// <summary>ISO 4217 alphabetic transactional currency code.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The transactional cash amount (must be strictly positive).</summary>
    public required decimal Amount { get; init; }

    /// <summary>The rate at <see cref="PaymentDate"/>; must be exactly <c>1.000000</c> for a base-currency payment.</summary>
    public required decimal ExchangeRate { get; init; }

    /// <summary>The cash/bank GL account the movement is recorded against (must exist and be active).</summary>
    public required int SettlementAccountId { get; init; }

    /// <summary>The date the cash moved (must not be in the future).</summary>
    public required DateTimeOffset PaymentDate { get; init; }

    /// <summary>Optional operator-supplied bank/transaction reference (at most 64 characters).</summary>
    public string? BankReference { get; init; }

    /// <summary>
    /// Base64-encoded <c>rowversion</c> token captured from the prior read, used for optimistic concurrency.
    /// A stale or malformed token is rejected with <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
