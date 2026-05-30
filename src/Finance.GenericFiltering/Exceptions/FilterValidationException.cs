namespace Finance.GenericFiltering.Exceptions;

/// <summary>
/// Thrown when a <see cref="Finance.GenericFiltering.Models.FilterRequest"/> is rejected
/// (unknown / non-filterable field, invalid operator, unparseable value, or oversized page).
/// Carries the matching <c>Finance.Common.ErrorCodes.FilterErrorCodes</c> constant so the
/// web layer can map it to a <c>400 ProblemDetails</c> per SDD-INFRA-001.
/// </summary>
public sealed class FilterValidationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FilterValidationException"/> class.
    /// </summary>
    /// <param name="errorCode">The machine-readable error code (a <c>FilterErrorCodes</c> constant).</param>
    /// <param name="detail">A developer-facing English description of the failure.</param>
    public FilterValidationException(string errorCode, string detail)
        : base(detail)
    {
        ErrorCode = errorCode;
        Detail = detail;
    }

    /// <summary>The machine-readable error code (SCREAMING_SNAKE_CASE) used as the ProblemDetails title.</summary>
    public string ErrorCode { get; }

    /// <summary>The developer-facing English detail describing the rejection.</summary>
    public string Detail { get; }
}
