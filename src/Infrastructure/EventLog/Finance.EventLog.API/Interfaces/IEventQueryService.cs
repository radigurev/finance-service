using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.ServiceModel.EventLog;

namespace Finance.EventLog.API.Interfaces;

/// <summary>
/// Read-only query service over the operational event-log archive (SDD-EVTLOG-001 §2.4-§2.5).
/// </summary>
public interface IEventQueryService
{
    /// <summary>
    /// Lists archived events as a filtered, sorted, and paged envelope. Defaults to ordering by
    /// <c>OccurredAt</c> descending when the caller supplies no sort, validates the optional
    /// <c>from</c>/<c>to</c> date range, and supports an optional <paramref name="correlationId"/>
    /// shortcut that returns every event in a single trace.
    /// </summary>
    /// <param name="request">The filter, sort, and pagination request.</param>
    /// <param name="correlationId">An optional correlation id that scopes the result to one trace.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the page, or a failure carrying the error code.</returns>
    Task<Result<PagedResult<EventLogEntryDto>>> SearchAsync(
        FilterRequest request,
        string? correlationId,
        CancellationToken cancellationToken);
}
