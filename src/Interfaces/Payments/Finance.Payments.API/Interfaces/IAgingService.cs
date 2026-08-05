using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.ServiceModel.Payments;

namespace Finance.Payments.API.Interfaces;

/// <summary>
/// The read-only AP/AR aging aggregation over the payments sub-ledger (SDD-PAY-003). Three query primitives turn
/// the SDD-PAY-002 <c>InvoiceOpenItem</c> projection — joined to the allocation and payment rows only where a
/// historical as-of date requires it — into an outstanding-balance view: the open-item drill-down, the bucketed
/// aging roll-up, and the per-counterparty balance summary.
/// <para><b>Nothing here writes.</b> No method changes a row, publishes an event, writes an audit row, runs a
/// workflow transition, or allocates a document number. Consequently the implementation injects no workflow
/// engine, no audit service, no publish endpoint, and no sequence generator.</para>
/// <para><b>Nothing here is cached.</b> Open items, aging, and counterparty balances are derived from
/// transactional data, so every call recomputes from the current projection state and no cache service is
/// injected (SDD-INFRA-004).</para>
/// <para><b>No cross-service read.</b> The aggregation source is the LOCAL projection only: the Invoices service
/// is never called and <c>finance_invoices</c> is never cross-database-joined. The read path is therefore
/// eventually consistent by construction, which is a documented property rather than a defect.</para>
/// <para>Every method returns a <see cref="Result"/> / <see cref="Result{T}"/> — an empty window and an unknown
/// counterparty are SUCCESSFUL empty payloads, never failures — and threads its
/// <see cref="CancellationToken"/> down to the query.</para>
/// </summary>
public interface IAgingService
{
    /// <summary>
    /// Lists the individual invoices that still carry an outstanding amount as of a date, each with its
    /// outstanding, base-currency outstanding, days past due, and bucket label (SDD-PAY-003 §2.5). The page runs
    /// through the SDD-INFRA-005 filter pipeline, caps its size at 200, and is ordered oldest-due-first with the
    /// projection key appended as the final deterministic sort term, so the list reads as a collection worklist.
    /// <para>Only items whose mirrored invoice status is <c>Confirmed</c> or <c>Posted</c>, whose document type
    /// some payment document type can settle, and whose outstanding amount is strictly positive are returned.</para>
    /// </summary>
    /// <param name="query">The optional as-of date, direction, counterparty, currency, and overdue-only narrowings.</param>
    /// <param name="request">The filter, sort, and pagination request from the query string.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A page of <see cref="OpenItemDto"/>, or a validation failure for a malformed narrowing.</returns>
    Task<Result<PagedResult<OpenItemDto>>> GetOpenItemsAsync(
        OpenItemQueryRequest query,
        FilterRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Computes the bucketed aging report for one direction as of a date (SDD-PAY-003 §2.6): the outstanding amount
    /// per counterparty per bucket, plus the report-level per-bucket totals in base currency. The whole report is
    /// ONE grouped round trip — never one query per counterparty and never one query per bucket.
    /// <para>A counterparty whose in-scope outstanding is <c>0.00</c> is omitted entirely; an empty window returns
    /// well-formed empty rows and zero totals with success.</para>
    /// </summary>
    /// <param name="query">The required as-of date and direction plus the optional counterparty, currency, and bucket boundaries.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The <see cref="AgingReportDto"/>, or a validation failure for a missing/future date, bad direction, or bad buckets.</returns>
    Task<Result<AgingReportDto>> GetAgingAsync(
        AgingReportQueryRequest query,
        CancellationToken cancellationToken);

    /// <summary>
    /// Summarizes each counterparty's outstanding and overdue position for one direction as of a date
    /// (SDD-PAY-003 §2.7), one row per (counterparty, currency) pair with its open-item count and oldest due date.
    /// <para>The overdue amount is the sum of every non-<c>Current</c> bucket, and the total outstanding is the
    /// SAME figure the aging report reports for the same pair: both surfaces read one shared aggregation path.</para>
    /// </summary>
    /// <param name="query">The required as-of date and direction plus the optional currency narrowing.</param>
    /// <param name="request">The pagination request from the query string; page size is capped at 200.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A page of <see cref="CounterpartyBalanceDto"/>, or a validation failure for a malformed narrowing.</returns>
    Task<Result<PagedResult<CounterpartyBalanceDto>>> GetCounterpartyBalancesAsync(
        CounterpartyBalanceQueryRequest query,
        FilterRequest request,
        CancellationToken cancellationToken);
}
