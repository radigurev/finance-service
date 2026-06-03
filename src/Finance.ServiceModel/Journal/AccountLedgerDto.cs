using Finance.GenericFiltering.Models;

namespace Finance.ServiceModel.Journal;

/// <summary>
/// The GL account-ledger response (SDD-FIN-003 §2.3): one account over a date window with an opening
/// balance (net of all <c>Posted</c> base debits − credits strictly before <c>FromDate</c>), the paged
/// in-window posted lines with their running balance, and the closing balance. An account with no posted
/// activity in or before the window yields zero balances and an empty page — never a 404 (SDD-FIN-003 §2.4).
/// </summary>
public sealed record AccountLedgerDto
{
    /// <summary>The account identifier the ledger was computed for.</summary>
    public required int AccountId { get; init; }

    /// <summary>The enriched account code, or <c>null</c> when the reference read was unavailable (SDD-FIN-003 §2.5).</summary>
    public string? AccountCode { get; init; }

    /// <summary>The enriched account name, or <c>null</c> when the reference read was unavailable (SDD-FIN-003 §2.5).</summary>
    public string? AccountName { get; init; }

    /// <summary>The inclusive lower bound of the window, or <c>null</c> when no lower bound was supplied (opening is then <c>0.00</c>).</summary>
    public DateTimeOffset? FromDate { get; init; }

    /// <summary>The inclusive upper bound of the window, or <c>null</c> when unbounded above.</summary>
    public DateTimeOffset? ToDate { get; init; }

    /// <summary>The net of all <c>Posted</c> base debits − credits strictly before <see cref="FromDate"/>; <c>0.00</c> when <see cref="FromDate"/> is omitted.</summary>
    public required decimal OpeningBalance { get; init; }

    /// <summary><see cref="OpeningBalance"/> plus the net of every in-window line (the running balance after the last in-window line).</summary>
    public required decimal ClosingBalance { get; init; }

    /// <summary>The paged in-window posted ledger lines, ordered by <c>EntryDate</c> ascending then line PK.</summary>
    public required PagedResult<AccountLedgerLineDto> Lines { get; init; }
}
