namespace Finance.Infrastructure.Caching.Interfaces;

/// <summary>
/// Validates that cache keys and scan patterns start with a registered <c>{service}:</c> prefix
/// (SDD-INFRA-004 §3). Used by <see cref="ICacheService{T}"/> implementations before every Redis call.
/// </summary>
public interface ICacheKeyValidator
{
    /// <summary>
    /// Ensures <paramref name="key"/> is non-empty and begins with a registered service prefix
    /// followed by a colon. Throws <see cref="CacheKeyPatternViolationException"/> otherwise.
    /// </summary>
    /// <param name="key">The cache key to validate.</param>
    void ValidateKey(string key);

    /// <summary>
    /// Ensures <paramref name="pattern"/> is non-empty and begins with a registered service prefix
    /// followed by a colon, so the multiplexer <c>SCAN</c> is never unbounded (SDD-INFRA-004 §2.3).
    /// Throws <see cref="CacheKeyPatternViolationException"/> otherwise.
    /// </summary>
    /// <param name="pattern">The scan pattern to validate.</param>
    void ValidatePattern(string pattern);
}
