using Finance.Common.ErrorCodes;

namespace Finance.Infrastructure.Caching;

/// <summary>
/// Thrown when a cache key or scan pattern does not start with a registered <c>{service}:</c> prefix
/// (SDD-INFRA-004 §3, §4). Carries the <see cref="CachingErrorCodes.CACHE_KEY_PATTERN_VIOLATION"/> code
/// so the web layer can surface it as a 500 ProblemDetails.
/// </summary>
public sealed class CacheKeyPatternViolationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CacheKeyPatternViolationException"/> class.
    /// </summary>
    /// <param name="message">A developer-facing description of the violated key or pattern.</param>
    public CacheKeyPatternViolationException(string message)
        : base(message)
    {
    }

    /// <summary>The machine-readable error code carried by this exception.</summary>
    public string ErrorCode => CachingErrorCodes.CACHE_KEY_PATTERN_VIOLATION;
}
