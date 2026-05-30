namespace Finance.Common.ErrorCodes;

/// <summary>
/// Machine-readable error codes for reference-data caching failures.
/// Used as the <c>title</c> field of ProblemDetails responses.
/// </summary>
public static class CachingErrorCodes
{
    /// <summary>The Redis cache could not be reached. Callers MUST fall through to the database.</summary>
    public const string REDIS_UNREACHABLE = nameof(REDIS_UNREACHABLE);

    /// <summary>A cache key did not conform to the required <c>{service}:{entity}:all</c> pattern.</summary>
    public const string CACHE_KEY_PATTERN_VIOLATION = nameof(CACHE_KEY_PATTERN_VIOLATION);
}
