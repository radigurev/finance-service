using Finance.Common.Enums;

namespace Finance.ServiceModel.Payments;

/// <summary>
/// Request body for creating a draft payment (SDD-PAY-001 §2.3). The base currency is sourced server-side
/// from the country strategy, the base amount is always recomputed server-side, and the direction is derived
/// from <see cref="DocumentType"/> — none of the three is part of the request.
/// </summary>
public sealed record CreatePaymentRequest
{
    /// <summary>The document type discriminating the two cash documents.</summary>
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
}
