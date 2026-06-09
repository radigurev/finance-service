namespace Finance.Common.Enums;

/// <summary>
/// Discriminates the four financial documents represented by the single <c>Invoice</c> aggregate
/// (SDD-INV-001 §1, §2.3). The value drives the document-number sequence key and prefix
/// (via <c>ICountryStrategy.GenerateDocumentNumber</c>), the derived <see cref="InvoiceDirection"/>,
/// and the posting-rule key carried on <c>InvoiceConfirmedEvent</c>.
/// </summary>
public enum InvoiceDocumentType
{
    /// <summary>A purchase (supplier) invoice — accounts payable.</summary>
    PurchaseInvoice = 1,

    /// <summary>A sale (customer) invoice — accounts receivable.</summary>
    SaleInvoice = 2,

    /// <summary>A credit note correcting (reducing) a previously issued document.</summary>
    CreditNote = 3,

    /// <summary>A debit note correcting (increasing) a previously issued document.</summary>
    DebitNote = 4
}
