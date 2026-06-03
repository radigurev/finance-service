namespace Finance.ServiceModel.Journal;

/// <summary>
/// A single per-account row of a trial balance (SDD-FIN-003 §2.2). Sums are computed over the account's
/// in-window <c>Posted</c> base-currency line amounts; the debit/credit column is derived purely from the
/// net sign (<c>TotalDebit − TotalCredit</c>), not from an account normal-balance classification.
/// </summary>
public sealed record TrialBalanceRowDto
{
    /// <summary>The posting-target account identifier (the aggregation key).</summary>
    public required int AccountId { get; init; }

    /// <summary>The enriched account code, or <c>null</c> when the reference read was unavailable (SDD-FIN-003 §2.5).</summary>
    public string? AccountCode { get; init; }

    /// <summary>The enriched account name, or <c>null</c> when the reference read was unavailable (SDD-FIN-003 §2.5).</summary>
    public string? AccountName { get; init; }

    /// <summary>The sum of the account's in-window <c>BaseDebitAmount</c> values.</summary>
    public required decimal TotalDebit { get; init; }

    /// <summary>The sum of the account's in-window <c>BaseCreditAmount</c> values.</summary>
    public required decimal TotalCredit { get; init; }

    /// <summary>The net placed in the debit column: <c>net</c> when <c>net ≥ 0</c>, otherwise <c>0.00</c>.</summary>
    public required decimal DebitBalance { get; init; }

    /// <summary>The net placed in the credit column: <c>−net</c> when <c>net &lt; 0</c>, otherwise <c>0.00</c>.</summary>
    public required decimal CreditBalance { get; init; }
}
