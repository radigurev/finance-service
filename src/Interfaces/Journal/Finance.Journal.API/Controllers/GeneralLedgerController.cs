using Asp.Versioning;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Web.Controllers;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Journal.API.Interfaces;
using Finance.ServiceModel.Journal;
using Microsoft.AspNetCore.Mvc;
using Warehouse.Auth.AspNetCore.Authorization;

namespace Finance.Journal.API.Controllers;

/// <summary>
/// Read-only REST endpoints for the General Ledger and Trial Balance (SDD-FIN-003): a live aggregation over
/// <c>Posted</c> journal-entry lines. Inherits <see cref="BaseApiController"/> so each action translates a
/// service <see cref="Result{T}"/> into an RFC 7807 ProblemDetails-aware <see cref="ActionResult"/>. Both
/// endpoints require <c>finance.journal:read</c> and never cache (SDD-INFRA-004).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[Produces("application/json")]
public sealed class GeneralLedgerController : BaseApiController
{
    private readonly IGeneralLedgerService _ledger;

    /// <summary>Creates a new <see cref="GeneralLedgerController"/>.</summary>
    /// <param name="ledger">The general-ledger aggregation service.</param>
    /// <param name="statusMap">The error-code → HTTP-status map used by the base controller.</param>
    public GeneralLedgerController(IGeneralLedgerService ledger, IErrorCodeToStatusMap statusMap)
        : base(statusMap)
    {
        _ledger = ledger;
    }

    /// <summary>Returns the trial balance as of a date, optionally bounded below by a from date (SDD-FIN-003 §2.2).</summary>
    /// <param name="asOfDate">The inclusive upper bound of the accounting entry date (required).</param>
    /// <param name="fromDate">The optional inclusive lower bound; cumulative from the beginning when omitted.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The <see cref="TrialBalanceDto"/>, or a 400 ProblemDetails for an invalid date window.</returns>
    [HttpGet("trial-balance")]
    [RequirePermission("finance.journal:read")]
    [ProducesResponseType(typeof(TrialBalanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TrialBalanceDto>> GetTrialBalance(
        [FromQuery] DateTimeOffset? asOfDate,
        [FromQuery] DateTimeOffset? fromDate,
        CancellationToken cancellationToken)
    {
        if (!asOfDate.HasValue)
        {
            return ToActionResult(Result<TrialBalanceDto>.Failure(
                JournalErrorCodes.INVALID_DATE_RANGE, "asOfDate is required."));
        }

        Result<TrialBalanceDto> result =
            await _ledger.GetTrialBalanceAsync(asOfDate.Value, fromDate, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Returns one account's ledger over a date window with running balances (SDD-FIN-003 §2.3).</summary>
    /// <param name="accountId">The account identifier (must be a positive integer).</param>
    /// <param name="fromDate">The optional inclusive lower bound of the window; opening is <c>0.00</c> when omitted.</param>
    /// <param name="toDate">The optional inclusive upper bound of the window.</param>
    /// <param name="request">The filter, sort, and pagination request for the in-window line list.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The <see cref="AccountLedgerDto"/>, or a 400 ProblemDetails for an invalid id/window/page.</returns>
    [HttpGet("general-ledger/accounts/{accountId:int}")]
    [RequirePermission("finance.journal:read")]
    [ProducesResponseType(typeof(AccountLedgerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AccountLedgerDto>> GetAccountLedger(
        int accountId,
        [FromQuery] DateTimeOffset? fromDate,
        [FromQuery] DateTimeOffset? toDate,
        [FromQuery] FilterRequest request,
        CancellationToken cancellationToken)
    {
        Result<AccountLedgerDto> result = await _ledger
            .GetAccountLedgerAsync(accountId, fromDate, toDate, request, cancellationToken)
            .ConfigureAwait(false);
        return ToActionResult(result);
    }
}
