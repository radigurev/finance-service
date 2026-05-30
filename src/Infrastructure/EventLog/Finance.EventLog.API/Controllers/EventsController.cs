using Asp.Versioning;
using Finance.Common.Results;
using Finance.EventLog.API.Interfaces;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Web.Controllers;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.ServiceModel.EventLog;
using Microsoft.AspNetCore.Mvc;
using Warehouse.Auth.AspNetCore.Authorization;

namespace Finance.EventLog.API.Controllers;

/// <summary>
/// Read-only REST endpoints for querying the operational event-log archive (SDD-EVTLOG-001 §2.4-§2.5).
/// Inherits <see cref="BaseApiController"/> so each action translates a service <see cref="Result"/> /
/// <see cref="Result{T}"/> into an RFC 7807 ProblemDetails-aware <see cref="ActionResult"/>.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/events")]
[Produces("application/json")]
public sealed class EventsController : BaseApiController
{
    private readonly IEventQueryService _events;

    /// <summary>Creates a new <see cref="EventsController"/>.</summary>
    /// <param name="events">The event-log query service.</param>
    /// <param name="statusMap">The error-code → HTTP-status map used by the base controller.</param>
    public EventsController(IEventQueryService events, IErrorCodeToStatusMap statusMap)
        : base(statusMap)
    {
        _events = events;
    }

    /// <summary>Lists archived events as a filtered, sorted, and paged envelope, defaulting to newest first.</summary>
    /// <param name="request">The filter, sort, and pagination request from the query string.</param>
    /// <param name="correlationId">An optional correlation id that scopes the result to a single trace.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A <see cref="PagedResult{T}"/> of <see cref="EventLogEntryDto"/>.</returns>
    [HttpGet]
    [RequirePermission("finance.event:read")]
    [ProducesResponseType(typeof(PagedResult<EventLogEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<EventLogEntryDto>>> List(
        [FromQuery] FilterRequest request,
        [FromQuery] string? correlationId,
        CancellationToken cancellationToken)
    {
        Result<PagedResult<EventLogEntryDto>> result =
            await _events.SearchAsync(request, correlationId, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }
}
