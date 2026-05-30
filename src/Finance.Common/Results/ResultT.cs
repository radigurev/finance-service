namespace Finance.Common.Results;

/// <summary>
/// Canonical value-bearing outcome type returned by Finance services. Carries a value
/// on success and a machine-readable error code on failure.
/// <para>See <see cref="Result"/> for the void variant.</para>
/// </summary>
/// <typeparam name="T">The type of the value produced on success.</typeparam>
/// <param name="IsSuccess">Whether the operation succeeded.</param>
/// <param name="Value">The produced value on success; <c>default</c> on failure.</param>
/// <param name="ErrorCode">The SCREAMING_SNAKE_CASE error code on failure; <c>null</c> on success.</param>
/// <param name="Detail">Optional developer-facing detail on failure; <c>null</c> on success.</param>
public sealed record Result<T>(bool IsSuccess, T? Value, string? ErrorCode, string? Detail)
{
    /// <summary>Creates a successful result carrying the supplied value.</summary>
    /// <param name="value">The value produced by the operation.</param>
    /// <returns>A success <see cref="Result{T}"/>.</returns>
    public static Result<T> Success(T value) => new(true, value, null, null);

    /// <summary>Creates a failed result carrying the supplied error code and optional detail.</summary>
    /// <param name="code">The machine-readable error code (a constant from <c>Finance.Common.ErrorCodes</c>).</param>
    /// <param name="detail">Optional developer-facing detail.</param>
    /// <returns>A failure <see cref="Result{T}"/> with a default value.</returns>
    public static Result<T> Failure(string code, string? detail = null) => new(false, default, code, detail);
}
