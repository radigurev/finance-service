using Asp.Versioning;
using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Web.Controllers;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Periods.API.Interfaces;
using Finance.ServiceModel.Periods;
using Microsoft.AspNetCore.Mvc;
using Warehouse.Auth.AspNetCore.Authorization;

namespace Finance.Periods.API.Controllers;

/// <summary>
/// REST endpoints for the fiscal-period lifecycle (SDD-FIN-004). Inherits <see cref="BaseApiController"/>
/// so every action translates a service <see cref="Result"/> / <see cref="Result{T}"/> into an RFC 7807
/// ProblemDetails-aware <see cref="ActionResult"/>. The <c>by-date</c> lookup is the contract the Journal
/// posting guard consumes (SDD-FIN-004 §2.6, §2.7).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/periods")]
[Produces("application/json")]
public sealed class FiscalPeriodsController : BaseApiController
{
    private readonly IFiscalPeriodService _periods;

    /// <summary>Creates a new <see cref="FiscalPeriodsController"/>.</summary>
    /// <param name="periods">The fiscal-period application service.</param>
    /// <param name="statusMap">The error-code → HTTP-status map used by the base controller.</param>
    public FiscalPeriodsController(IFiscalPeriodService periods, IErrorCodeToStatusMap statusMap)
        : base(statusMap)
    {
        _periods = periods;
    }

    /// <summary>Lists fiscal periods as a filtered, sorted, and paged envelope (SDD-FIN-004 §2.11).</summary>
    /// <param name="request">The filter, sort, and pagination request from the query string.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A <see cref="PagedResult{T}"/> of <see cref="FiscalPeriodDto"/>.</returns>
    [HttpGet]
    [RequirePermission("finance.period:read")]
    [ProducesResponseType(typeof(PagedResult<FiscalPeriodDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<FiscalPeriodDto>>> List(
        [FromQuery] FilterRequest request,
        CancellationToken cancellationToken)
    {
        Result<PagedResult<FiscalPeriodDto>> result =
            await _periods.SearchAsync(request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Returns a single fiscal period by surrogate id (SDD-FIN-004 §2.11).</summary>
    /// <param name="id">The period identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The matching <see cref="FiscalPeriodDto"/>, or a 404 ProblemDetails.</returns>
    [HttpGet("{id:int}")]
    [RequirePermission("finance.period:read")]
    [ProducesResponseType(typeof(FiscalPeriodDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FiscalPeriodDto>> Get(int id, CancellationToken cancellationToken)
    {
        Result<FiscalPeriodDto> result = await _periods.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Returns the period whose inclusive date bounds contain the supplied date (SDD-FIN-004 §2.6).</summary>
    /// <param name="date">The date to resolve to a period.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The containing <see cref="FiscalPeriodDto"/>, or <c>NO_PERIOD_FOR_DATE</c> (404).</returns>
    [HttpGet("by-date")]
    [RequirePermission("finance.period:read")]
    [ProducesResponseType(typeof(FiscalPeriodDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FiscalPeriodDto>> GetByDate(
        [FromQuery] DateTimeOffset date,
        CancellationToken cancellationToken)
    {
        Result<FiscalPeriodDto> result = await _periods.GetByDateAsync(date, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Returns the period identified by its natural key (SDD-FIN-004 §2.6).</summary>
    /// <param name="fiscalYear">The accounting year.</param>
    /// <param name="periodNumber">The 1-based period ordinal.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The matching <see cref="FiscalPeriodDto"/>, or a 404 ProblemDetails.</returns>
    [HttpGet("by-year-number")]
    [RequirePermission("finance.period:read")]
    [ProducesResponseType(typeof(FiscalPeriodDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FiscalPeriodDto>> GetByYearNumber(
        [FromQuery] int fiscalYear,
        [FromQuery] int periodNumber,
        CancellationToken cancellationToken)
    {
        Result<FiscalPeriodDto> result =
            await _periods.GetByYearNumberAsync(fiscalYear, periodNumber, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Generates the full set of fiscal periods for a year (SDD-FIN-004 §2.2).</summary>
    /// <param name="request">The generation request body.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The generated periods, or a duplicate / validation ProblemDetails.</returns>
    [HttpPost("generate")]
    [RequirePermission("finance.period:create")]
    [ProducesResponseType(typeof(IReadOnlyList<FiscalPeriodDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IReadOnlyList<FiscalPeriodDto>>> Generate(
        GeneratePeriodsRequest request,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<FiscalPeriodDto>> result =
            await _periods.GenerateAsync(request, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return ToActionResult(result);
        }

        return CreatedAtAction(nameof(List), null, result.Value);
    }

    /// <summary>Creates a single fiscal period explicitly (SDD-FIN-004 §2.3).</summary>
    /// <param name="request">The create request body.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The created <see cref="FiscalPeriodDto"/>, or a duplicate / overlap / validation ProblemDetails.</returns>
    [HttpPost]
    [RequirePermission("finance.period:create")]
    [ProducesResponseType(typeof(FiscalPeriodDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FiscalPeriodDto>> Create(
        CreatePeriodRequest request,
        CancellationToken cancellationToken)
    {
        Result<FiscalPeriodDto> result = await _periods.CreateAsync(request, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return ToActionResult(result);
        }

        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id, version = "1" }, result.Value);
    }

    /// <summary>Closes an open fiscal period (Open → Closed) (SDD-FIN-004 §2.4).</summary>
    /// <param name="id">The period identifier.</param>
    /// <param name="request">The close request carrying the mandatory reason and row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The closed <see cref="FiscalPeriodDto"/>, or a state / ordering / concurrency ProblemDetails.</returns>
    [HttpPost("{id:int}/close")]
    [RequirePermission("finance.period:close")]
    [ProducesResponseType(typeof(FiscalPeriodDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FiscalPeriodDto>> Close(
        int id,
        ClosePeriodRequest request,
        CancellationToken cancellationToken)
    {
        Result<FiscalPeriodDto> result = await _periods.CloseAsync(id, request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Reopens a closed fiscal period (Closed → Open) (SDD-FIN-004 §2.5).</summary>
    /// <param name="id">The period identifier.</param>
    /// <param name="request">The reopen request carrying the mandatory reason and row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The reopened <see cref="FiscalPeriodDto"/>, or a state / ordering / concurrency ProblemDetails.</returns>
    [HttpPost("{id:int}/reopen")]
    [RequirePermission("finance.period:reopen")]
    [ProducesResponseType(typeof(FiscalPeriodDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FiscalPeriodDto>> Reopen(
        int id,
        ReopenPeriodRequest request,
        CancellationToken cancellationToken)
    {
        Result<FiscalPeriodDto> result = await _periods.ReopenAsync(id, request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }
}
