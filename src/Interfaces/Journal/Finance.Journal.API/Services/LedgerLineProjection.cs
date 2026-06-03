namespace Finance.Journal.API.Services;

/// <summary>
/// Internal flat projection of an in-window posted ledger line carrying the parent-entry display fields
/// alongside the line's base-currency amounts (SDD-FIN-003 §2.3). The running balance is computed in memory
/// over the ordered page; this projection itself carries no running balance. Not exposed through the API.
/// </summary>
internal sealed record LedgerLineProjection
{
    /// <summary>The line primary key (the deterministic final sort key).</summary>
    public required int LineId { get; init; }

    /// <summary>The owning entry's gapless document number.</summary>
    public required string EntryNumber { get; init; }

    /// <summary>The owning entry's accounting date.</summary>
    public required DateTimeOffset EntryDate { get; init; }

    /// <summary>The owning entry's memo.</summary>
    public required string EntryDescription { get; init; }

    /// <summary>The optional per-line memo (takes precedence over the entry memo when present).</summary>
    public string? LineDescription { get; init; }

    /// <summary>The line's base-currency debit amount.</summary>
    public required decimal Debit { get; init; }

    /// <summary>The line's base-currency credit amount.</summary>
    public required decimal Credit { get; init; }
}
