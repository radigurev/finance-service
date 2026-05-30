namespace Finance.Common.Results;

/// <summary>
/// Canonical non-generic outcome type returned by Finance services for operations
/// that produce no value. Carries a machine-readable error code on failure.
/// <para>See <see cref="Result{T}"/> for the value-bearing variant.</para>
/// </summary>
/// <param name="IsSuccess">Whether the operation succeeded.</param>
/// <param name="ErrorCode">The SCREAMING_SNAKE_CASE error code on failure; <c>null</c> on success.</param>
/// <param name="Detail">Optional developer-facing detail on failure; <c>null</c> on success.</param>
public sealed record Result(bool IsSuccess, string? ErrorCode, string? Detail)
{
    /// <summary>Creates a successful result with no error code or detail.</summary>
    /// <returns>A success <see cref="Result"/>.</returns>
    public static Result Success() => new(true, null, null);

    /// <summary>Creates a failed result carrying the supplied error code and optional detail.</summary>
    /// <param name="code">The machine-readable error code (a constant from <c>Finance.Common.ErrorCodes</c>).</param>
    /// <param name="detail">Optional developer-facing detail.</param>
    /// <returns>A failure <see cref="Result"/>.</returns>
    public static Result Failure(string code, string? detail = null) => new(false, code, detail);
}
