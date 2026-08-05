namespace Finance.Common.Enums;

/// <summary>
/// The ledger direction of a <c>Payment</c> (SDD-PAY-001 §2.3). Derived from
/// <see cref="PaymentDocumentType"/> and frozen at creation: <c>CustomerReceipt</c> is <see cref="AR"/>;
/// <c>SupplierPayment</c> is <see cref="AP"/>.
/// <para>The member set and the numeric values mirror <see cref="InvoiceDirection"/> value-for-value
/// (<c>AP = 1</c>, <c>AR = 2</c>) so a direction-match guard can compare the two enums without a
/// translation table (SDD-PAY-001 §7).</para>
/// </summary>
public enum PaymentDirection
{
    /// <summary>Accounts payable — money the business pays out (supplier payment).</summary>
    AP = 1,

    /// <summary>Accounts receivable — money the business receives (customer receipt).</summary>
    AR = 2
}
