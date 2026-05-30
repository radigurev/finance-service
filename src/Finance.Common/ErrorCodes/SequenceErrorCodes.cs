namespace Finance.Common.ErrorCodes;

/// <summary>
/// Machine-readable error codes for gapless document sequence generation failures.
/// Used as the <c>title</c> field of ProblemDetails responses.
/// </summary>
public static class SequenceErrorCodes
{
    /// <summary>The requested sequence key has not been configured.</summary>
    public const string UNKNOWN_SEQUENCE_KEY = nameof(UNKNOWN_SEQUENCE_KEY);

    /// <summary>A gap was detected in an otherwise gapless sequence.</summary>
    public const string SEQUENCE_GAP_DETECTED = nameof(SEQUENCE_GAP_DETECTED);
}
