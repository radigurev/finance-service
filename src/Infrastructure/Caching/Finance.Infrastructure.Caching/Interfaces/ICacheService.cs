namespace Finance.Infrastructure.Caching.Interfaces;

/// <summary>
/// Cache-aside abstraction over the shared Redis instance for a single value type
/// (SDD-INFRA-004 §2). Implementations MUST fall through to the supplied factory and
/// MUST NEVER throw when Redis is unreachable.
/// </summary>
/// <typeparam name="T">The cached value type. Serialized with System.Text.Json in v1.</typeparam>
public interface ICacheService<T>
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or invokes <paramref name="factory"/>,
    /// stores its result, and returns it (cache-aside). When Redis is unreachable the factory is
    /// invoked and its result is returned without caching (SDD-INFRA-004 §2.5).
    /// </summary>
    /// <param name="key">A key that MUST start with a registered <c>{service}:</c> prefix.</param>
    /// <param name="factory">The loader invoked on a cache miss or when Redis is unavailable.</param>
    /// <param name="ttl">
    /// Optional time-to-live. When <c>null</c> the data-class default is applied. Must fall within
    /// <c>[1 second, 24 hours]</c> (SDD-INFRA-004 §3).
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the factory and the cache operations.</param>
    /// <returns>The cached or freshly loaded value.</returns>
    Task<T?> GetOrSetAsync(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evicts a single cache entry. A failure to reach Redis is logged and swallowed
    /// (SDD-INFRA-004 §2.5); the method never throws on connection failure.
    /// </summary>
    /// <param name="key">A key that MUST start with a registered <c>{service}:</c> prefix.</param>
    /// <param name="cancellationToken">Token used to cancel the cache operation.</param>
    /// <returns>A task that completes when the eviction attempt finishes.</returns>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evicts every key matching <paramref name="prefixedPattern"/> using a multiplexer-level
    /// <c>SCAN</c> bounded by a registered <c>{service}:</c> prefix (SDD-INFRA-004 §2.3). The pattern
    /// MUST be prefixed by a registered service segment; an unbounded pattern is rejected.
    /// </summary>
    /// <param name="prefixedPattern">A glob pattern that MUST begin with a registered <c>{service}:</c> prefix.</param>
    /// <param name="cancellationToken">Token used to cancel the scan and eviction.</param>
    /// <returns>A task that completes when the eviction attempt finishes.</returns>
    Task RemoveByPatternAsync(string prefixedPattern, CancellationToken cancellationToken = default);
}
