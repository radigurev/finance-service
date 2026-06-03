namespace Finance.ServiceModel.Journal;

/// <summary>
/// The trial-balance response (SDD-FIN-003 §2.2): every account with in-window <c>Posted</c> activity, its
/// debit/credit column placement, the grand totals, and the <c>Balanced</c> invariant. Because every posted
/// entry balances in base currency (SDD-FIN-001 §2.3), <see cref="Balanced"/> MUST be <see langword="true"/>
/// over a consistent ledger; a <see langword="false"/> value signals corruption and is surfaced, not corrected.
/// </summary>
public sealed record TrialBalanceDto
{
    /// <summary>The inclusive upper bound of the accounting <c>EntryDate</c> the balance was computed at.</summary>
    public required DateTimeOffset AsOfDate { get; init; }

    /// <summary>The inclusive lower bound of the window, or <c>null</c> when the balance is cumulative from the beginning.</summary>
    public DateTimeOffset? FromDate { get; init; }

    /// <summary>The per-account rows, ordered by <c>AccountCode</c> ascending (falling back to <c>AccountId</c>).</summary>
    public required IReadOnlyList<TrialBalanceRowDto> Rows { get; init; }

    /// <summary>The sum of every row's <see cref="TrialBalanceRowDto.DebitBalance"/>.</summary>
    public required decimal GrandTotalDebit { get; init; }

    /// <summary>The sum of every row's <see cref="TrialBalanceRowDto.CreditBalance"/>.</summary>
    public required decimal GrandTotalCredit { get; init; }

    /// <summary>
    /// Whether <see cref="GrandTotalDebit"/> equals <see cref="GrandTotalCredit"/> to the cent
    /// (SDD-FIN-003 §2.2). True over a consistent ledger; false signals ledger corruption.
    /// </summary>
    public required bool Balanced { get; init; }
}
