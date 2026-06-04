using Finance.Country.Abstractions;

namespace Finance.ServiceModel.Posting;

/// <summary>
/// Request body for a single line of a posting-rule create/update (SDD-FIN-006 §3.1).
/// </summary>
public sealed record CreatePostingRuleLineRequest
{
    /// <summary>The chart-of-accounts code this line posts to.</summary>
    public required string AccountSelector { get; init; }

    /// <summary>Whether the line books its resolved amount on the debit or credit side.</summary>
    public required PostingDebitOrCredit DebitOrCredit { get; init; }

    /// <summary>Which amount from the apply-time context feeds this line.</summary>
    public required PostingAmountSource AmountSource { get; init; }
}
