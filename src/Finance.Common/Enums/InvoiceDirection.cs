namespace Finance.Common.Enums;

/// <summary>
/// The ledger direction of an <c>Invoice</c> (SDD-INV-001 §2.3). Derived from
/// <see cref="InvoiceDocumentType"/> and frozen at creation: <c>SaleInvoice</c>/<c>DebitNote</c> are
/// <see cref="AR"/>; <c>PurchaseInvoice</c>/<c>CreditNote</c> are <see cref="AP"/>.
/// </summary>
public enum InvoiceDirection
{
    /// <summary>Accounts payable — money the business owes (purchase invoice / credit note).</summary>
    AP = 1,

    /// <summary>Accounts receivable — money owed to the business (sale invoice / debit note).</summary>
    AR = 2
}
