namespace Finance.Common.ErrorCodes;

/// <summary>
/// Cross-cutting machine-readable error codes shared by every Finance domain.
/// Used as the <c>title</c> field of ProblemDetails responses and in
/// FluentValidation <c>.WithErrorCode(...)</c> calls.
/// </summary>
public static class CommonErrorCodes
{
    /// <summary>An unclassified failure with no more specific code available.</summary>
    public const string GENERIC_ERROR = nameof(GENERIC_ERROR);

    /// <summary>One or more shape, range, or business validations failed.</summary>
    public const string VALIDATION_FAILED = nameof(VALIDATION_FAILED);

    /// <summary>A row version mismatch was detected during a concurrent write. Single source for this code.</summary>
    public const string CONCURRENT_MODIFICATION = nameof(CONCURRENT_MODIFICATION);
}
