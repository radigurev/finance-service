namespace Finance.ServiceModel.Events.Journal;

/// <summary>
/// Immutable line payload embedded in <see cref="JournalEntryPostedEvent"/> (SDD-FIN-002 §2.11,
/// SDD-INFRA-006 §2.2). Carries the posted line's account, amounts, currency, rate, and base amounts.
/// </summary>
public sealed record JournalEntryPostedLine
{
    /// <summary>The posting-target account identifier.</summary>
    public required int AccountId { get; init; }

    /// <summary>The transactional debit amount.</summary>
    public required decimal DebitAmount { get; init; }

    /// <summary>The transactional credit amount.</summary>
    public required decimal CreditAmount { get; init; }

    /// <summary>ISO 4217 alphabetic code of the line's transactional currency.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The rate from the line currency to the entry's base currency.</summary>
    public required decimal ExchangeRate { get; init; }

    /// <summary>The base-currency equivalent of the debit amount.</summary>
    public required decimal BaseDebitAmount { get; init; }

    /// <summary>The base-currency equivalent of the credit amount.</summary>
    public required decimal BaseCreditAmount { get; init; }
}
