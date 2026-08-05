namespace Finance.Common.Enums;

/// <summary>
/// Records how the cash physically moved for a <c>Payment</c> (SDD-PAY-001 §1, §2.3). Deliberately
/// three-valued in v1: a <c>Compensation</c>/offset method would be a breaking enum change
/// (SDD-PAY-001 §5). The method does NOT affect the document-number prefix in v1 — prefixing by method is
/// a deferred open item (SDD-PAY-001 §7).
/// </summary>
public enum PaymentMethod
{
    /// <summary>Cash paid or received over the counter.</summary>
    Cash = 1,

    /// <summary>A bank transfer.</summary>
    BankTransfer = 2,

    /// <summary>A card payment.</summary>
    Card = 3
}
