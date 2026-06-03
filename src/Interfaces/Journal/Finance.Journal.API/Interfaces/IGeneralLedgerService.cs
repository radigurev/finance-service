using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.ServiceModel.Journal;

namespace Finance.Journal.API.Interfaces;

/// <summary>
/// Read-only aggregation service over <c>Posted</c> journal-entry lines (SDD-FIN-003). It computes the
/// trial balance and per-account ledgers as a live <c>SELECT … GROUP BY</c> over <c>finance_journal</c> —
/// it owns no tables, writes no audit rows, publishes no events, and MUST NOT be cached (SDD-INFRA-004).
/// Every method returns a <see cref="Result{T}"/>; business outcomes are never signalled via <c>null</c>
/// or thrown exceptions (SDD-INFRA-009). All arithmetic is in base currency, <c>decimal</c> only.
/// </summary>
public interface IGeneralLedgerService
{
    /// <summary>
    /// Builds the trial balance for an as-of date and optional from date (SDD-FIN-003 §2.2): every account
    /// with in-window <c>Posted</c> activity, summed base debits/credits, net column placement, grand totals,
    /// and the <c>Balanced</c> invariant. Account code/name are enriched via the reference seam; enrichment
    /// failure degrades to null code/name without failing the query (SDD-FIN-003 §2.5).
    /// </summary>
    /// <param name="asOfDate">The inclusive upper bound of the accounting <c>EntryDate</c>.</param>
    /// <param name="fromDate">The optional inclusive lower bound; cumulative from the beginning when omitted.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the trial balance, or an <c>INVALID_DATE_RANGE</c> failure.</returns>
    Task<Result<TrialBalanceDto>> GetTrialBalanceAsync(
        DateTimeOffset asOfDate,
        DateTimeOffset? fromDate,
        CancellationToken cancellationToken);

    /// <summary>
    /// Builds a single account's ledger over a date window (SDD-FIN-003 §2.3): an opening balance (net of
    /// all <c>Posted</c> base debits − credits strictly before <paramref name="fromDate"/>), the paged
    /// in-window posted lines with their running balance (SDD-INFRA-005), and the closing balance. An account
    /// with no posted activity yields zero balances and an empty page — never a 404 (SDD-FIN-003 §2.4, the
    /// resolved empty-ledger default; no account-existence pre-check is performed).
    /// </summary>
    /// <param name="accountId">The account identifier (MUST be a positive integer).</param>
    /// <param name="fromDate">The optional inclusive lower bound of the window; opening is <c>0.00</c> when omitted.</param>
    /// <param name="toDate">The optional inclusive upper bound of the window.</param>
    /// <param name="request">The filter, sort, and pagination request for the in-window line list.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the ledger, or an <c>INVALID_ACCOUNT_ID</c> / <c>INVALID_DATE_RANGE</c> / paging failure.</returns>
    Task<Result<AccountLedgerDto>> GetAccountLedgerAsync(
        int accountId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        FilterRequest request,
        CancellationToken cancellationToken);
}
