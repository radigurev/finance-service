namespace Finance.ServiceModel.Journal;

/// <summary>
/// Representation of a single journal entry line exposed by the Journal API (SDD-FIN-001 §2.2).
/// Exactly one of <see cref="DebitAmount"/> / <see cref="CreditAmount"/> is non-zero; the balance
/// invariant is asserted on the base-currency amounts.
/// </summary>
public sealed record JournalEntryLineDto
{
    /// <summary>Internal surrogate identifier of the line.</summary>
    public required int Id { get; init; }

    /// <summary>The posting-target account identifier (SDD-ACCT-001).</summary>
    public required int AccountId { get; init; }

    /// <summary>The transactional debit amount; zero when the line is a credit.</summary>
    public required decimal DebitAmount { get; init; }

    /// <summary>The transactional credit amount; zero when the line is a debit.</summary>
    public required decimal CreditAmount { get; init; }

    /// <summary>ISO 4217 alphabetic code of the line's transactional currency.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The rate from the line currency to the entry's base currency (<c>1.000000</c> for base-currency lines).</summary>
    public required decimal ExchangeRate { get; init; }

    /// <summary>The base-currency equivalent of <see cref="DebitAmount"/>.</summary>
    public required decimal BaseDebitAmount { get; init; }

    /// <summary>The base-currency equivalent of <see cref="CreditAmount"/>.</summary>
    public required decimal BaseCreditAmount { get; init; }

    /// <summary>The 1-based ordinal for stable display ordering.</summary>
    public required int LineNumber { get; init; }

    /// <summary>Optional per-line memo.</summary>
    public string? Description { get; init; }
}
