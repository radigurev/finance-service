namespace Finance.Country.Abstractions;

/// <summary>
/// One line of a <see cref="PostingRuleTemplate"/> (SDD-CTRY-001 §2.2). A plain immutable data contract
/// shared by the producing <c>ICountryStrategy</c> and the consuming Posting-Rule seeder (SDD-FIN-006) —
/// it carries no behavior.
/// </summary>
public sealed record PostingRuleLineTemplate
{
    /// <summary>
    /// The account this line posts to, expressed as a chart-of-accounts <c>code</c> string in v1
    /// (e.g. <c>"411"</c>). The code is resolved to a postable account identifier by the consumer at
    /// apply time against SDD-ACCT-001 (SDD-CTRY-001 §7, SDD-FIN-006 §2.2).
    /// </summary>
    public required string AccountSelector { get; init; }

    /// <summary>Whether the line books its resolved amount on the debit or credit side.</summary>
    public required PostingDebitOrCredit DebitOrCredit { get; init; }

    /// <summary>Which amount from the apply-time context feeds this line.</summary>
    public required PostingAmountSource AmountSource { get; init; }
}
