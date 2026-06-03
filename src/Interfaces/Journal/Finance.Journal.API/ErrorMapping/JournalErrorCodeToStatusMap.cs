using Finance.Common.ErrorCodes;
using Finance.Infrastructure.Web.ErrorMapping;
using Microsoft.AspNetCore.Http;

namespace Finance.Journal.API.ErrorMapping;

/// <summary>
/// Journal-domain extension of <see cref="DefaultErrorCodeToStatusMap"/> (SDD-FIN-001 §4, SDD-FIN-002 §4).
/// The default suffix/pattern rules do not classify the Journal state-conflict codes
/// (<c>ACCOUNT_NOT_POSTABLE</c>, <c>ENTRY_NOT_DRAFT</c>, <c>CANNOT_EDIT_POSTED_ENTRY</c>,
/// <c>INVALID_JOURNAL_STATE_TRANSITION</c>, <c>POSTING_PERIOD_CLOSED</c>) as 409, so this map adds them
/// and delegates every other code to the default map.
/// </summary>
public sealed class JournalErrorCodeToStatusMap : IErrorCodeToStatusMap
{
    private static readonly IReadOnlySet<string> ConflictCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        JournalErrorCodes.ACCOUNT_NOT_POSTABLE,
        JournalErrorCodes.ENTRY_NOT_DRAFT,
        JournalErrorCodes.CANNOT_EDIT_POSTED_ENTRY,
        JournalErrorCodes.INVALID_JOURNAL_STATE_TRANSITION,
        JournalErrorCodes.POSTING_PERIOD_CLOSED
    };

    private readonly DefaultErrorCodeToStatusMap _default = new();

    /// <inheritdoc />
    public int MapToStatus(string errorCode)
    {
        if (!string.IsNullOrEmpty(errorCode) && ConflictCodes.Contains(errorCode))
        {
            return StatusCodes.Status409Conflict;
        }

        return _default.MapToStatus(errorCode);
    }
}
