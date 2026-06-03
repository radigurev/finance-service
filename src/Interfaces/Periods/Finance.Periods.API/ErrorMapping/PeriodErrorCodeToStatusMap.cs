using Finance.Common.ErrorCodes;
using Finance.Infrastructure.Web.ErrorMapping;
using Microsoft.AspNetCore.Http;

namespace Finance.Periods.API.ErrorMapping;

/// <summary>
/// Periods-domain extension of <see cref="DefaultErrorCodeToStatusMap"/> (SDD-FIN-004 §5). The default
/// suffix/pattern rules do not classify the Periods state / ordering / uniqueness conflict codes
/// (<c>PERIOD_ALREADY_CLOSED</c>, <c>PERIOD_ALREADY_OPEN</c>, <c>INVALID_PERIOD_STATE_TRANSITION</c>,
/// <c>CANNOT_CLOSE_OUT_OF_ORDER</c>, <c>OVERLAPPING_PERIOD</c>, <c>DUPLICATE_PERIOD</c>) as 409, so this map
/// adds them, maps <c>NO_PERIOD_FOR_DATE</c> to 404, and delegates every other code to the default map.
/// </summary>
public sealed class PeriodErrorCodeToStatusMap : IErrorCodeToStatusMap
{
    private static readonly IReadOnlySet<string> ConflictCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        PeriodErrorCodes.PERIOD_ALREADY_CLOSED,
        PeriodErrorCodes.PERIOD_ALREADY_OPEN,
        PeriodErrorCodes.INVALID_PERIOD_STATE_TRANSITION,
        PeriodErrorCodes.CANNOT_CLOSE_OUT_OF_ORDER,
        PeriodErrorCodes.OVERLAPPING_PERIOD,
        PeriodErrorCodes.DUPLICATE_PERIOD
    };

    private readonly DefaultErrorCodeToStatusMap _default = new();

    /// <inheritdoc />
    public int MapToStatus(string errorCode)
    {
        if (string.IsNullOrEmpty(errorCode))
        {
            return _default.MapToStatus(errorCode);
        }

        if (ConflictCodes.Contains(errorCode))
        {
            return StatusCodes.Status409Conflict;
        }

        if (string.Equals(errorCode, PeriodErrorCodes.NO_PERIOD_FOR_DATE, StringComparison.Ordinal))
        {
            return StatusCodes.Status404NotFound;
        }

        return _default.MapToStatus(errorCode);
    }
}
