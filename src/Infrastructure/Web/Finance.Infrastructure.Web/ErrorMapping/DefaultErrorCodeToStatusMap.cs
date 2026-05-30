using Microsoft.AspNetCore.Http;

namespace Finance.Infrastructure.Web.ErrorMapping;

/// <summary>
/// Default suffix / pattern based error-code → HTTP-status mapping per SDD-INFRA-009 §2.4:
/// <c>*_NOT_FOUND</c> → 404; <c>*_INACTIVE</c> / <c>*_DUPLICATE*</c> / <c>*_CONFLICT</c> /
/// <c>CONCURRENT_*</c> → 409; <c>*_FORBIDDEN</c> / <c>INSUFFICIENT_*</c> → 403;
/// <c>*_UNREACHABLE</c> → 503; anything else → 400.
/// </summary>
public sealed class DefaultErrorCodeToStatusMap : IErrorCodeToStatusMap
{
    /// <inheritdoc />
    public int MapToStatus(string errorCode)
    {
        if (string.IsNullOrEmpty(errorCode))
        {
            return StatusCodes.Status400BadRequest;
        }

        if (errorCode.EndsWith("_NOT_FOUND", StringComparison.Ordinal))
        {
            return StatusCodes.Status404NotFound;
        }

        if (IsConflictFamily(errorCode))
        {
            return StatusCodes.Status409Conflict;
        }

        if (errorCode.EndsWith("_FORBIDDEN", StringComparison.Ordinal) ||
            errorCode.StartsWith("INSUFFICIENT_", StringComparison.Ordinal))
        {
            return StatusCodes.Status403Forbidden;
        }

        if (errorCode.EndsWith("_UNREACHABLE", StringComparison.Ordinal))
        {
            return StatusCodes.Status503ServiceUnavailable;
        }

        return StatusCodes.Status400BadRequest;
    }

    private static bool IsConflictFamily(string errorCode)
    {
        return errorCode.EndsWith("_INACTIVE", StringComparison.Ordinal)
            || errorCode.Contains("DUPLICATE", StringComparison.Ordinal)
            || errorCode.EndsWith("_CONFLICT", StringComparison.Ordinal)
            || errorCode.StartsWith("CONCURRENT_", StringComparison.Ordinal);
    }
}
