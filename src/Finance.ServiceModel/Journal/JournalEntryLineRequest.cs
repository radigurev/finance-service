namespace Finance.ServiceModel.Journal;

/// <summary>
/// Request body for a single line of a journal entry create/update (SDD-FIN-001 §2.2). The caller
/// supplies the transactional amount, currency, rate, and the pre-computed base-currency amounts; the
/// engine validates the reconciliation (SDD-FIN-001 §2.7).
/// </summary>
public sealed record JournalEntryLineRequest
{
    /// <summary>The posting-target account identifier (SDD-ACCT-001).</summary>
    public required int AccountId { get; init; }

    /// <summary>The transactional debit amount; zero when the line is a credit.</summary>
    public decimal DebitAmount { get; init; }

    /// <summary>The transactional credit amount; zero when the line is a debit.</summary>
    public decimal CreditAmount { get; init; }

    /// <summary>ISO 4217 alphabetic code of the line's transactional currency.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The rate from the line currency to the entry's base currency (<c>1.000000</c> for base-currency lines).</summary>
    public required decimal ExchangeRate { get; init; }

    /// <summary>The base-currency equivalent of <see cref="DebitAmount"/>.</summary>
    public decimal BaseDebitAmount { get; init; }

    /// <summary>The base-currency equivalent of <see cref="CreditAmount"/>.</summary>
    public decimal BaseCreditAmount { get; init; }

    /// <summary>Optional per-line memo.</summary>
    public string? Description { get; init; }
}
