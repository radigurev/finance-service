namespace Finance.Common.ErrorCodes;

/// <summary>
/// Machine-readable error codes for the event-log query domain.
/// Used as the <c>title</c> field of ProblemDetails responses.
/// <para>Oversized page requests reuse <see cref="FilterErrorCodes.PAGE_SIZE_TOO_LARGE"/>.</para>
/// </summary>
public static class EventLogErrorCodes
{
    /// <summary>The supplied date range is invalid (e.g. the start is after the end).</summary>
    public const string INVALID_DATE_RANGE = nameof(INVALID_DATE_RANGE);

    /// <summary>The requested event-log entry does not exist.</summary>
    public const string EVENT_NOT_FOUND = nameof(EVENT_NOT_FOUND);
}
