using Microsoft.AspNetCore.Mvc;

namespace Finance.Infrastructure.Web.ProblemDetails;

/// <summary>
/// Builds RFC 7807 <see cref="ProblemDetails"/> instances with the Finance conventions of
/// SDD-INFRA-001 §2.2: <c>title</c> = machine code, <c>detail</c> = developer English,
/// <c>type</c> = <c>https://finance.local/errors/{code}</c>.
/// </summary>
public static class FinanceProblemDetailsBuilder
{
    /// <summary>The base URI used to build the ProblemDetails <c>type</c> field.</summary>
    public const string ErrorTypeBaseUri = "https://finance.local/errors/";

    /// <summary>
    /// Builds a ProblemDetails carrying the supplied status, error code, and detail. When no detail
    /// is supplied the error code is humanized into a readable English fallback.
    /// </summary>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="errorCode">The machine-readable error code used as the title and type suffix.</param>
    /// <param name="detail">The developer-facing detail; humanized from the code when <see langword="null"/>.</param>
    /// <returns>A populated <see cref="ProblemDetails"/>.</returns>
    public static Microsoft.AspNetCore.Mvc.ProblemDetails Build(int status, string errorCode, string? detail)
    {
        return new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = status,
            Title = errorCode,
            Detail = detail ?? Humanize(errorCode),
            Type = ErrorTypeBaseUri + errorCode
        };
    }

    /// <summary>Converts a SCREAMING_SNAKE_CASE code into a capitalized, space-separated phrase.</summary>
    /// <param name="errorCode">The error code to humanize.</param>
    /// <returns>A human-readable rendering of the code.</returns>
    public static string Humanize(string errorCode)
    {
        if (string.IsNullOrEmpty(errorCode))
        {
            return string.Empty;
        }

        string lowered = errorCode.Replace('_', ' ').ToLowerInvariant();
        return char.ToUpperInvariant(lowered[0]) + lowered[1..] + ".";
    }
}
