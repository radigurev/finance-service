using Asp.Versioning;
using Finance.Common.Results;
using Finance.Infrastructure.Web.Controllers;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Payments.API.Interfaces;
using Finance.ServiceModel.Payments;
using Microsoft.AspNetCore.Mvc;
using Warehouse.Auth.AspNetCore.Authorization;

namespace Finance.Payments.API.Controllers;

/// <summary>
/// The read-only bucketed AP/AR aging report (SDD-PAY-003 §2.6). Inherits <see cref="BaseApiController"/> so the
/// action translates the service <see cref="Result{T}"/> into an RFC 7807 ProblemDetails-aware
/// <see cref="ActionResult"/>.
/// <para>It requires the report-level <c>finance.aging:read</c> permission rather than
/// <c>finance.payment:read</c>, so a finance-reporting or collections role can be granted the roll-up without
/// being granted the individual payment records. That permission is NEW: while SDD-INT-AUTH-001 permission
/// auto-registration remains deferred it MUST be seeded manually in the auth service, or every caller receives
/// <c>403</c>.</para>
/// <para>Nothing here is cached: aging is derived from transactional data, so every request recomputes from the
/// current projection state (SDD-INFRA-004). The report is also period-status-agnostic — a closed period's
/// invoices are still aged.</para>
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/aging")]
[Produces("application/json")]
public sealed class AgingController : BaseApiController
{
    private readonly IAgingService _aging;

    /// <summary>Creates a new <see cref="AgingController"/>.</summary>
    /// <param name="aging">The aging aggregation service.</param>
    /// <param name="statusMap">The error-code → HTTP-status map used by the base controller.</param>
    public AgingController(IAgingService aging, IErrorCodeToStatusMap statusMap)
        : base(statusMap)
    {
        _aging = aging;
    }

    /// <summary>
    /// Returns the bucketed aging report for one direction as of a date: the outstanding amount per counterparty per
    /// bucket, plus the report-level per-bucket totals in base currency (SDD-PAY-003 §2.6).
    /// <para>Rows are keyed by the PAIR (counterparty, currency), so a multi-currency counterparty produces two
    /// rows and only the base-currency column is cross-summable. Rows are ordered by total base outstanding
    /// descending, then counterparty, then currency. A counterparty whose in-scope outstanding is <c>0.00</c> is
    /// omitted entirely, and an empty window returns empty rows with zero totals and a <c>200</c> — an unknown
    /// counterparty is deliberately never pre-checked, because the counterparty is Warehouse-owned master data.</para>
    /// <para>Buckets default to <c>Current</c>, <c>1-30</c>, <c>31-60</c>, <c>61-90</c>, <c>90+</c> from the day
    /// boundaries <c>30, 60, 90</c>; a caller may pass up to six strictly ascending positive boundaries instead. The
    /// effective boundaries and labels are echoed on the response, and each bucket carries its own numeric bounds, so
    /// a client never re-derives either.</para>
    /// <para>For a HISTORICAL as-of date the settled amount is replayed from the invoice's surviving allocation rows
    /// by allocation date; because a deallocation removes its row, that is the sub-ledger as it stands now replayed
    /// backwards, not a bi-temporal audit reconstruction.</para>
    /// </summary>
    /// <param name="query">The required as-of date and direction plus the optional counterparty, currency, and bucket boundaries.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The <see cref="AgingReportDto"/>, or a 400 ProblemDetails.</returns>
    [HttpGet]
    [RequirePermission("finance.aging:read")]
    [ProducesResponseType(typeof(AgingReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AgingReportDto>> Get(
        [FromQuery] AgingReportQueryRequest query,
        CancellationToken cancellationToken)
    {
        Result<AgingReportDto> result = await _aging
            .GetAgingAsync(query, cancellationToken)
            .ConfigureAwait(false);
        return ToActionResult(result);
    }
}
