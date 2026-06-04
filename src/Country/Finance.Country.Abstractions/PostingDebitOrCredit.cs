namespace Finance.Country.Abstractions;

/// <summary>
/// The side a posting-rule line books to (SDD-CTRY-001 §2.2). A balanceable template MUST contain at
/// least one <see cref="Debit"/> and one <see cref="Credit"/> line; the per-context numeric balance is
/// enforced by the Posting Engine at apply time (SDD-FIN-006 §2.4).
/// </summary>
public enum PostingDebitOrCredit
{
    /// <summary>The line books the resolved amount on the debit side of the entry.</summary>
    Debit,

    /// <summary>The line books the resolved amount on the credit side of the entry.</summary>
    Credit
}
