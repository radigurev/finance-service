namespace Finance.Common.Enums;

/// <summary>
/// Discriminates the two cash documents represented by the single <c>Payment</c> aggregate
/// (SDD-PAY-001 §1, §2.3). The value drives the gapless sequence key (<c>RCT</c>/<c>PAY</c>) and the
/// document-number prefix via <c>ICountryStrategy.GenerateDocumentNumber</c>, the derived and frozen
/// <see cref="PaymentDirection"/>, and the posting-rule key carried on <c>PaymentConfirmedEvent</c>.
/// Deliberately two-valued in v1: a refund document type would be a breaking enum change
/// (SDD-PAY-001 §5).
/// </summary>
public enum PaymentDocumentType
{
    /// <summary>A customer receipt — money in, accounts receivable.</summary>
    CustomerReceipt = 1,

    /// <summary>A supplier payment — money out, accounts payable.</summary>
    SupplierPayment = 2
}
