using Finance.Common.Enums;
using Finance.Infrastructure.Sequences;

namespace Finance.Payments.API.Services;

/// <summary>
/// Maps a <see cref="PaymentDocumentType"/> to its gapless sequence key (SDD-INFRA-003 §2.1), its derived and
/// frozen <see cref="PaymentDirection"/> (SDD-PAY-001 §2.3), and its posting-rule key (SDD-PAY-001 §2.5,
/// §2.13; SDD-FIN-006). The maps are the single source of these per-type discriminators inside the Payments
/// service.
/// <para>The <c>RCT</c>/<c>PAY</c> sequence keys and their definitions already exist in SDD-INFRA-003 and MUST
/// NOT be redefined, renamed, or re-prefixed here.</para>
/// </summary>
public static class PaymentDocumentTypeMap
{
    /// <summary>Posting-rule key for customer receipts (<c>Dr 503</c> / <c>Cr 411</c>).</summary>
    public const string CustomerReceiptRuleKey = "PAYMENT_CUSTOMER_RECEIPT";

    /// <summary>Posting-rule key for supplier payments (<c>Dr 401</c> / <c>Cr 503</c>).</summary>
    public const string SupplierPaymentRuleKey = "PAYMENT_SUPPLIER_PAYMENT";

    /// <summary>
    /// Resolves the gapless sequence key (<c>RCT</c>/<c>PAY</c>) for the document type.
    /// </summary>
    /// <param name="documentType">The payment document type.</param>
    /// <returns>The registered sequence key for the document type.</returns>
    public static string SequenceKeyFor(PaymentDocumentType documentType) => documentType switch
    {
        PaymentDocumentType.CustomerReceipt => SequenceKeys.Receipt,
        PaymentDocumentType.SupplierPayment => SequenceKeys.Payment,
        _ => throw new ArgumentOutOfRangeException(nameof(documentType), documentType, null)
    };

    /// <summary>
    /// Resolves the frozen <see cref="PaymentDirection"/> for the document type
    /// (<c>CustomerReceipt</c> → <c>AR</c>; <c>SupplierPayment</c> → <c>AP</c>). A client-supplied direction is
    /// always ignored in favour of this derivation.
    /// </summary>
    /// <param name="documentType">The payment document type.</param>
    /// <returns>The ledger direction for the document type.</returns>
    public static PaymentDirection DirectionFor(PaymentDocumentType documentType) => documentType switch
    {
        PaymentDocumentType.CustomerReceipt => PaymentDirection.AR,
        PaymentDocumentType.SupplierPayment => PaymentDirection.AP,
        _ => throw new ArgumentOutOfRangeException(nameof(documentType), documentType, null)
    };

    /// <summary>
    /// Determines whether the supplied document type is one of the two kinds SDD-PAY-001 §2.3 defines,
    /// so a caller can return <c>INVALID_PAYMENT_DOCUMENT_TYPE</c> as a business outcome instead of
    /// letting <see cref="DirectionFor(PaymentDocumentType)"/> throw (SDD-INFRA-009: a service returns
    /// <c>Result</c> for a business failure and never throws). The HTTP path is already screened by
    /// <c>CreatePaymentRequestValidator</c>; this guard covers the non-HTTP callers §2.3 anticipates.
    /// </summary>
    /// <param name="documentType">The payment document type to test.</param>
    /// <returns><c>true</c> when the document type is supported; otherwise <c>false</c>.</returns>
    public static bool IsSupported(PaymentDocumentType documentType) =>
        documentType is PaymentDocumentType.CustomerReceipt or PaymentDocumentType.SupplierPayment;

    /// <summary>
    /// Resolves the posting-rule key carried on <c>PaymentConfirmedEvent</c> for the document type.
    /// </summary>
    /// <param name="documentType">The payment document type.</param>
    /// <returns>The posting-rule key for the document type.</returns>
    public static string PostingRuleKeyFor(PaymentDocumentType documentType) => documentType switch
    {
        PaymentDocumentType.CustomerReceipt => CustomerReceiptRuleKey,
        PaymentDocumentType.SupplierPayment => SupplierPaymentRuleKey,
        _ => throw new ArgumentOutOfRangeException(nameof(documentType), documentType, null)
    };
}
