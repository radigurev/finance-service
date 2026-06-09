using Finance.Common.Enums;
using Finance.Infrastructure.Sequences;

namespace Finance.Invoices.API.Services;

/// <summary>
/// Maps an <see cref="InvoiceDocumentType"/> to its gapless sequence key (SDD-INFRA-003 §2.1), its derived
/// <see cref="InvoiceDirection"/> (SDD-INV-001 §2.3), and its posting-rule key (SDD-INV-001 §2.5,
/// SDD-FIN-006). The maps are the single source of these per-type discriminators inside the Invoice service.
/// </summary>
public static class InvoiceDocumentTypeMap
{
    /// <summary>Posting-rule key for sale invoices.</summary>
    public const string SaleInvoiceRuleKey = "SALE_INVOICE";

    /// <summary>Posting-rule key for purchase invoices.</summary>
    public const string PurchaseInvoiceRuleKey = "PURCHASE_INVOICE";

    /// <summary>Posting-rule key for credit notes.</summary>
    public const string CreditNoteRuleKey = "CREDIT_NOTE";

    /// <summary>Posting-rule key for debit notes.</summary>
    public const string DebitNoteRuleKey = "DEBIT_NOTE";

    /// <summary>
    /// Resolves the gapless sequence key (<c>PINV</c>/<c>SINV</c>/<c>CN</c>/<c>DN</c>) for the document type.
    /// </summary>
    /// <param name="documentType">The invoice document type.</param>
    /// <returns>The registered sequence key for the document type.</returns>
    public static string SequenceKeyFor(InvoiceDocumentType documentType) => documentType switch
    {
        InvoiceDocumentType.PurchaseInvoice => SequenceKeys.PurchaseInvoice,
        InvoiceDocumentType.SaleInvoice => SequenceKeys.SaleInvoice,
        InvoiceDocumentType.CreditNote => SequenceKeys.CreditNote,
        InvoiceDocumentType.DebitNote => SequenceKeys.DebitNote,
        _ => throw new ArgumentOutOfRangeException(nameof(documentType), documentType, null)
    };

    /// <summary>
    /// Resolves the frozen <see cref="InvoiceDirection"/> for the document type
    /// (<c>SaleInvoice</c>/<c>DebitNote</c> → <c>AR</c>; <c>PurchaseInvoice</c>/<c>CreditNote</c> → <c>AP</c>).
    /// </summary>
    /// <param name="documentType">The invoice document type.</param>
    /// <returns>The ledger direction for the document type.</returns>
    public static InvoiceDirection DirectionFor(InvoiceDocumentType documentType) => documentType switch
    {
        InvoiceDocumentType.SaleInvoice => InvoiceDirection.AR,
        InvoiceDocumentType.DebitNote => InvoiceDirection.AR,
        InvoiceDocumentType.PurchaseInvoice => InvoiceDirection.AP,
        InvoiceDocumentType.CreditNote => InvoiceDirection.AP,
        _ => throw new ArgumentOutOfRangeException(nameof(documentType), documentType, null)
    };

    /// <summary>
    /// Resolves the posting-rule key carried on <c>InvoiceConfirmedEvent</c> for the document type.
    /// </summary>
    /// <param name="documentType">The invoice document type.</param>
    /// <returns>The posting-rule key for the document type.</returns>
    public static string PostingRuleKeyFor(InvoiceDocumentType documentType) => documentType switch
    {
        InvoiceDocumentType.SaleInvoice => SaleInvoiceRuleKey,
        InvoiceDocumentType.PurchaseInvoice => PurchaseInvoiceRuleKey,
        InvoiceDocumentType.CreditNote => CreditNoteRuleKey,
        InvoiceDocumentType.DebitNote => DebitNoteRuleKey,
        _ => throw new ArgumentOutOfRangeException(nameof(documentType), documentType, null)
    };
}
