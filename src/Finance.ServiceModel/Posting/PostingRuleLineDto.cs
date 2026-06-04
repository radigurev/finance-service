using Finance.Country.Abstractions;

namespace Finance.ServiceModel.Posting;

/// <summary>
/// Representation of a single posting-rule line exposed by the Journal API (SDD-FIN-006 §2.1).
/// </summary>
public sealed record PostingRuleLineDto
{
    /// <summary>Surrogate identifier of the line.</summary>
    public required int Id { get; init; }

    /// <summary>The 1-based position of the line within the rule.</summary>
    public required int LineNumber { get; init; }

    /// <summary>The chart-of-accounts code this line posts to (resolved to an account id at apply time).</summary>
    public required string AccountSelector { get; init; }

    /// <summary>Whether the line books its resolved amount on the debit or credit side.</summary>
    public required PostingDebitOrCredit DebitOrCredit { get; init; }

    /// <summary>Which amount from the apply-time context feeds this line.</summary>
    public required PostingAmountSource AmountSource { get; init; }
}
