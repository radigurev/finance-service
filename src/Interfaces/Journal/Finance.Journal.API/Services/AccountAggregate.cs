namespace Finance.Journal.API.Services;

/// <summary>
/// Internal per-account aggregate of summed base-currency debit/credit totals produced by the trial-balance
/// <c>GROUP BY</c> (SDD-FIN-003 §2.2). Not exposed through the API.
/// </summary>
internal sealed record AccountAggregate
{
    /// <summary>The account identifier (the aggregation key).</summary>
    public required int AccountId { get; init; }

    /// <summary>The summed in-window <c>BaseDebitAmount</c>.</summary>
    public required decimal TotalDebit { get; init; }

    /// <summary>The summed in-window <c>BaseCreditAmount</c>.</summary>
    public required decimal TotalCredit { get; init; }
}
