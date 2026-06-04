namespace Finance.Country.Abstractions;

/// <summary>
/// Names which monetary amount from the apply-time context feeds a posting-rule line
/// (SDD-CTRY-001 §2.2, SDD-FIN-006 §2.3). The v1 set is the minimum the Bulgaria sample rules need; the
/// enum is additive — new values (e.g. <c>Discount</c>, <c>Rounding</c>) are non-breaking (SDD-FIN-006 §5).
/// </summary>
public enum PostingAmountSource
{
    /// <summary>The net (pre-tax) amount of the transaction.</summary>
    Net,

    /// <summary>The tax (VAT) amount of the transaction.</summary>
    Tax,

    /// <summary>The gross (net + tax) amount of the transaction.</summary>
    Gross
}
