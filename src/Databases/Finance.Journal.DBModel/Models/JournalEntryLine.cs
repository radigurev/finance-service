namespace Finance.Journal.DBModel.Models;

/// <summary>
/// A single debit-or-credit line of a <see cref="JournalEntry"/> (SDD-FIN-001 §2.2). Exactly one of
/// <see cref="DebitAmount"/> / <see cref="CreditAmount"/> is non-zero; the double-entry balance is
/// asserted on the base-currency amounts. A line has no independent lifecycle (composition).
/// </summary>
public sealed class JournalEntryLine
{
    /// <summary>Internal surrogate identifier (not externally exposed).</summary>
    public int Id { get; set; }

    /// <summary>Foreign key to the owning <see cref="JournalEntry"/>.</summary>
    public Guid JournalEntryId { get; set; }

    /// <summary>Navigation to the owning entry.</summary>
    public JournalEntry? JournalEntry { get; set; }

    /// <summary>The posting-target account identifier (SDD-ACCT-001; no cross-database join).</summary>
    public int AccountId { get; set; }

    /// <summary>The transactional debit amount; zero when the line is a credit.</summary>
    public decimal DebitAmount { get; set; }

    /// <summary>The transactional credit amount; zero when the line is a debit.</summary>
    public decimal CreditAmount { get; set; }

    /// <summary>ISO 4217 alphabetic code of the line's transactional currency.</summary>
    public required string CurrencyCode { get; set; }

    /// <summary>The rate from the line currency to the entry's base currency (<c>1.000000</c> for base lines).</summary>
    public decimal ExchangeRate { get; set; }

    /// <summary>The base-currency equivalent of <see cref="DebitAmount"/>.</summary>
    public decimal BaseDebitAmount { get; set; }

    /// <summary>The base-currency equivalent of <see cref="CreditAmount"/>.</summary>
    public decimal BaseCreditAmount { get; set; }

    /// <summary>The 1-based ordinal for stable display ordering.</summary>
    public int LineNumber { get; set; }

    /// <summary>Optional per-line memo.</summary>
    public string? Description { get; set; }
}
