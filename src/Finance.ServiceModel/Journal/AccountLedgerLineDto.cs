namespace Finance.ServiceModel.Journal;

/// <summary>
/// A single line of a GL account ledger (SDD-FIN-003 §2.3): one posted journal-entry line projected onto an
/// account, carrying its base-currency debit/credit and the running balance up to and including this line.
/// </summary>
public sealed record AccountLedgerLineDto
{
    /// <summary>The internal surrogate identifier of the underlying journal-entry line (the deterministic final sort key).</summary>
    public required int LineId { get; init; }

    /// <summary>The owning entry's gapless document number (always present, since only <c>Posted</c> lines appear).</summary>
    public required string EntryNumber { get; init; }

    /// <summary>The accounting date of the owning entry.</summary>
    public required DateTimeOffset EntryDate { get; init; }

    /// <summary>The owning entry's memo (the per-line memo takes precedence when present).</summary>
    public string? Description { get; init; }

    /// <summary>The line's base-currency debit amount.</summary>
    public required decimal Debit { get; init; }

    /// <summary>The line's base-currency credit amount.</summary>
    public required decimal Credit { get; init; }

    /// <summary>The opening balance plus the cumulative (<c>Debit − Credit</c>) up to and including this line, in ledger order.</summary>
    public required decimal RunningBalance { get; init; }
}
