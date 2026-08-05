using Asp.Versioning;
using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Web.Controllers;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Payments.API.Interfaces;
using Finance.ServiceModel.Payments;
using Microsoft.AspNetCore.Mvc;
using Warehouse.Auth.AspNetCore.Authorization;

namespace Finance.Payments.API.Controllers;

/// <summary>
/// The read-only per-counterparty outstanding and overdue summary (SDD-PAY-003 §2.7). Inherits
/// <see cref="BaseApiController"/> so the action translates the service <see cref="Result{T}"/> into an RFC 7807
/// ProblemDetails-aware <see cref="ActionResult"/>.
/// <para>Like the aging report it requires the report-level <c>finance.aging:read</c> permission, which is NEW and
/// MUST be seeded manually in the auth service while SDD-INT-AUTH-001 permission auto-registration remains
/// deferred.</para>
/// <para>Nothing here is cached: balances are derived from transactional data, so every request recomputes from the
/// current projection state (SDD-INFRA-004).</para>
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/counterparty-balances")]
[Produces("application/json")]
public sealed class CounterpartyBalancesController : BaseApiController
{
    private readonly IAgingService _aging;

    /// <summary>Creates a new <see cref="CounterpartyBalancesController"/>.</summary>
    /// <param name="aging">The aging aggregation service.</param>
    /// <param name="statusMap">The error-code → HTTP-status map used by the base controller.</param>
    public CounterpartyBalancesController(IAgingService aging, IErrorCodeToStatusMap statusMap)
        : base(statusMap)
    {
        _aging = aging;
    }

    /// <summary>
    /// Returns one row per (counterparty, currency) pair for one direction as of a date, carrying the total
    /// outstanding, the overdue subset, the open-item count, and the oldest due date (SDD-PAY-003 §2.7).
    /// <para>The overdue amount is exactly the sum of every non-<c>Current</c> aging bucket, and the total
    /// outstanding is the SAME figure <c>GET /api/v1/aging</c> reports for the same pair, as-of date, and direction:
    /// both endpoints read one shared aggregation path so they cannot drift.</para>
    /// <para>A counterparty with zero outstanding is omitted from the page and is not counted in the total count; an
    /// unknown counterparty simply yields an empty page with a <c>200</c>. The page size is capped at 200 and rows
    /// are ordered by base outstanding descending, then by the composite (counterparty, currency) grouping key,
    /// which is the only key a grouped row has.</para>
    /// <para>The view is invoice-only in v1: unallocated payment cash is NOT netted in, so a counterparty sitting on
    /// on-account cash still shows its full invoice outstanding and no balance is ever negative.</para>
    /// </summary>
    /// <param name="query">The required as-of date and direction plus the optional currency narrowing.</param>
    /// <param name="request">The pagination request from the query string.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A <see cref="PagedResult{T}"/> of <see cref="CounterpartyBalanceDto"/>, or a 400 ProblemDetails.</returns>
    [HttpGet]
    [RequirePermission("finance.aging:read")]
    [ProducesResponseType(typeof(PagedResult<CounterpartyBalanceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<CounterpartyBalanceDto>>> List(
        [FromQuery] CounterpartyBalanceQueryRequest query,
        [FromQuery] FilterRequest request,
        CancellationToken cancellationToken)
    {
        Result<PagedResult<CounterpartyBalanceDto>> result = await _aging
            .GetCounterpartyBalancesAsync(query, request, cancellationToken)
            .ConfigureAwait(false);
        return ToActionResult(result);
    }
}
