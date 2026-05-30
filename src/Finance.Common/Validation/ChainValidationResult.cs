namespace Finance.Common.Validation;

/// <summary>
/// Outcome of a single chain validator or of the composed <see cref="ValidationChain{TRequest}"/>.
/// Carries a machine-readable error code and optional detail when validation fails.
/// </summary>
/// <param name="IsValid">Whether validation succeeded.</param>
/// <param name="ErrorCode">The error code on failure; <c>null</c> when valid.</param>
/// <param name="Detail">Optional developer-facing detail on failure; <c>null</c> when valid.</param>
public readonly record struct ChainValidationResult(bool IsValid, string? ErrorCode, string? Detail)
{
    /// <summary>Creates a successful validation result.</summary>
    /// <returns>A valid <see cref="ChainValidationResult"/>.</returns>
    public static ChainValidationResult Success() => new(true, null, null);

    /// <summary>Creates a failed validation result carrying the supplied error code and optional detail.</summary>
    /// <param name="code">The machine-readable error code (a constant from <c>Finance.Common.ErrorCodes</c>).</param>
    /// <param name="detail">Optional developer-facing detail.</param>
    /// <returns>An invalid <see cref="ChainValidationResult"/>.</returns>
    public static ChainValidationResult Failure(string code, string? detail = null) => new(false, code, detail);
}
