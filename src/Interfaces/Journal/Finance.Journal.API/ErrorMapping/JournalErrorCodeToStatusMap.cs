using Finance.Common.ErrorCodes;
using Finance.Infrastructure.Web.ErrorMapping;
using Microsoft.AspNetCore.Http;

namespace Finance.Journal.API.ErrorMapping;

/// <summary>
/// Journal-domain extension of <see cref="DefaultErrorCodeToStatusMap"/> (SDD-FIN-001 §4, SDD-FIN-002 §4,
/// SDD-FIN-006 §4). The default suffix/pattern rules do not classify the Journal state-conflict codes
/// (<c>ACCOUNT_NOT_POSTABLE</c>, <c>ENTRY_NOT_DRAFT</c>, <c>CANNOT_EDIT_POSTED_ENTRY</c>,
/// <c>INVALID_JOURNAL_STATE_TRANSITION</c>, <c>POSTING_PERIOD_CLOSED</c>) nor the posting-rule conflict
/// codes (<c>DUPLICATE_POSTING_RULE_KEY</c>, <c>POSTING_RULE_UNBALANCED</c>) as 409, nor
/// <c>POSTING_RULE_ACCOUNT_NOT_FOUND</c> as 422, so this map adds them and delegates every other code to
/// the default map (where <c>*_NOT_FOUND</c> → 404 and the remainder → 400).
/// </summary>
public sealed class JournalErrorCodeToStatusMap : IErrorCodeToStatusMap
{
    private static readonly IReadOnlySet<string> ConflictCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        JournalErrorCodes.ACCOUNT_NOT_POSTABLE,
        JournalErrorCodes.ENTRY_NOT_DRAFT,
        JournalErrorCodes.CANNOT_EDIT_POSTED_ENTRY,
        JournalErrorCodes.INVALID_JOURNAL_STATE_TRANSITION,
        JournalErrorCodes.POSTING_PERIOD_CLOSED,
        PostingErrorCodes.DUPLICATE_POSTING_RULE_KEY,
        PostingErrorCodes.POSTING_RULE_UNBALANCED
    };

    private static readonly IReadOnlySet<string> UnprocessableCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        PostingErrorCodes.POSTING_RULE_ACCOUNT_NOT_FOUND
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

        if (UnprocessableCodes.Contains(errorCode))
        {
            return StatusCodes.Status422UnprocessableEntity;
        }

        return _default.MapToStatus(errorCode);
    }
}
