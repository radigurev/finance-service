using Finance.Country.Abstractions;

namespace Finance.Journal.DBModel.Models;

/// <summary>
/// One line of a <see cref="PostingRule"/> (SDD-FIN-006 §2.1). Maps a single whole
/// <see cref="PostingAmountSource"/> from the apply-time context onto one account
/// (<see cref="AccountSelector"/>) on the <see cref="DebitOrCredit"/> side. The account is held as a
/// chart-of-accounts <c>code</c> string and resolved to a postable account identifier at apply time
/// (SDD-FIN-006 §2.2). The reserved nullable <see cref="Percentage"/>/<see cref="FixedAmount"/> columns
/// are inert in v1 (split-line support is deferred — SDD-FIN-006 §5).
/// </summary>
public sealed class PostingRuleLine
{
    /// <summary>Surrogate identifier.</summary>
    public int Id { get; set; }

    /// <summary>The owning rule's surrogate identifier.</summary>
    public int PostingRuleId { get; set; }

    /// <summary>Navigation to the owning rule.</summary>
    public PostingRule? PostingRule { get; set; }

    /// <summary>The 1-based position of the line within the rule.</summary>
    public int LineNumber { get; set; }

    /// <summary>The chart-of-accounts <c>code</c> string this line posts to (resolved to an account id at apply time).</summary>
    public required string AccountSelector { get; set; }

    /// <summary>Whether the line books its resolved amount on the debit or credit side.</summary>
    public PostingDebitOrCredit DebitOrCredit { get; set; }

    /// <summary>Which amount from the apply-time context feeds this line.</summary>
    public PostingAmountSource AmountSource { get; set; }

    /// <summary>Reserved for split-line support; <c>null</c> and ignored in v1 (SDD-FIN-006 §5).</summary>
    public decimal? Percentage { get; set; }

    /// <summary>Reserved for split-line support; <c>null</c> and ignored in v1 (SDD-FIN-006 §5).</summary>
    public decimal? FixedAmount { get; set; }
}
