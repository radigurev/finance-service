using Finance.Infrastructure.Caching.Interfaces;

namespace Finance.Periods.API.Tests.Fixtures;

/// <summary>
/// In-memory <see cref="ICacheService{T}"/> double used by the Periods unit tests (SDD-FIN-004 §2.8,
/// SDD-INFRA-004). It always falls through to the supplied factory (cache-miss semantics, so reads reflect
/// the live database) and records every <c>RemoveByPatternAsync</c> invalidation pattern so tests can
/// assert that close / reopen / generate / create invalidate the bounded <c>finance-periods:*</c> region.
/// </summary>
/// <typeparam name="T">The cached value type.</typeparam>
public sealed class RecordingCacheService<T> : ICacheService<T>
{
    /// <summary>The patterns passed to <see cref="RemoveByPatternAsync"/>, in call order.</summary>
    public List<string> RemovedPatterns { get; } = [];

    /// <summary>The single keys passed to <see cref="RemoveAsync"/>, in call order.</summary>
    public List<string> RemovedKeys { get; } = [];

    /// <inheritdoc />
    public Task<T?> GetOrSetAsync(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory(cancellationToken);
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        RemovedKeys.Add(key);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveByPatternAsync(string prefixedPattern, CancellationToken cancellationToken = default)
    {
        RemovedPatterns.Add(prefixedPattern);
        return Task.CompletedTask;
    }
}
