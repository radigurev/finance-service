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
/// The read-only open-item drill-down behind a single aging cell (SDD-PAY-003 §2.5). Inherits
/// <see cref="BaseApiController"/> so the action translates the service <see cref="Result{T}"/> into an RFC 7807
/// ProblemDetails-aware <see cref="ActionResult"/>.
/// <para>It requires <c>finance.payment:read</c> — the same permission that reads payments, because an open item is
/// a projection the Payments service already owns. The two ROLL-UP endpoints require the separate
/// <c>finance.aging:read</c> instead, so a collections role can be granted the reports without the individual
/// payment records.</para>
/// <para>Nothing here is cached: open items are derived from transactional data, so every request recomputes from
/// the current projection state (SDD-INFRA-004).</para>
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/open-items")]
[Produces("application/json")]
public sealed class OpenItemsController : BaseApiController
{
    private readonly IAgingService _aging;

    /// <summary>Creates a new <see cref="OpenItemsController"/>.</summary>
    /// <param name="aging">The aging aggregation service.</param>
    /// <param name="statusMap">The error-code → HTTP-status map used by the base controller.</param>
    public OpenItemsController(IAgingService aging, IErrorCodeToStatusMap statusMap)
        : base(statusMap)
    {
        _aging = aging;
    }

    /// <summary>
    /// Lists the invoices that still carry an outstanding amount as of a date, each with its outstanding amount, its
    /// base-currency counterpart at the invoice's frozen booking rate, its days past due, and its aging bucket label
    /// (SDD-PAY-003 §2.5).
    /// <para>The page runs through the SDD-INFRA-005 filter pipeline, caps its size at 200, and is ordered
    /// OLDEST-DUE-FIRST with the projection key appended as the final deterministic sort term, so it reads as a
    /// collection worklist and pages stably. An empty window is a <c>200</c> with no items, never a <c>404</c>.</para>
    /// <para>Only documents that are legally in force (<c>Confirmed</c>/<c>Posted</c>) and that some payment
    /// document type can actually settle appear. A confirmed CREDIT NOTE is therefore absent permanently and by
    /// design, not pending event consumption; the projection is otherwise eventually consistent, so a very recently
    /// confirmed invoice may be missing and a very recently cancelled or reversed one may still appear.</para>
    /// </summary>
    /// <param name="query">The optional as-of date, direction, counterparty, currency, and overdue-only narrowings.</param>
    /// <param name="request">The filter, sort, and pagination request from the query string.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A <see cref="PagedResult{T}"/> of <see cref="OpenItemDto"/>, or a 400 ProblemDetails.</returns>
    [HttpGet]
    [RequirePermission("finance.payment:read")]
    [ProducesResponseType(typeof(PagedResult<OpenItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<OpenItemDto>>> List(
        [FromQuery] OpenItemQueryRequest query,
        [FromQuery] FilterRequest request,
        CancellationToken cancellationToken)
    {
        Result<PagedResult<OpenItemDto>> result = await _aging
            .GetOpenItemsAsync(query, request, cancellationToken)
            .ConfigureAwait(false);
        return ToActionResult(result);
    }
}
