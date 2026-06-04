using Finance.Infrastructure.Caching.Interfaces;
using Finance.ServiceModel.Posting;

namespace Finance.Journal.API.Tests.Fixtures;

/// <summary>
/// In-memory cache-aside substitute for <see cref="ICacheService{T}"/> used by the posting-rule CRUD unit
/// tests (SDD-FIN-006 §2.7). It serves a cached single-rule value on a hit and records every
/// pattern-invalidation so a test can assert a write evicted <c>finance-journal:posting-rule:*</c> and a
/// later read fell through to the database. It never throws, mirroring the Redis-down fall-through contract.
/// </summary>
public sealed class RecordingPostingRuleCacheService : ICacheService<PostingRuleDto>
{
    private readonly Dictionary<string, PostingRuleDto> _store = new(StringComparer.Ordinal);

    /// <summary>The cache keys that were factory-loaded (cache misses), in call order.</summary>
    public List<string> FactoryLoads { get; } = [];

    /// <summary>The invalidation patterns passed to <see cref="RemoveByPatternAsync"/>, in call order.</summary>
    public List<string> InvalidationPatterns { get; } = [];

    /// <inheritdoc />
    public async Task<PostingRuleDto?> GetOrSetAsync(
        string key,
        Func<CancellationToken, Task<PostingRuleDto?>> factory,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(key, out PostingRuleDto? cached))
        {
            return cached;
        }

        FactoryLoads.Add(key);
        PostingRuleDto? loaded = await factory(cancellationToken).ConfigureAwait(false);
        if (loaded is not null)
        {
            _store[key] = loaded;
        }

        return loaded;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _store.Remove(key);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveByPatternAsync(string prefixedPattern, CancellationToken cancellationToken = default)
    {
        InvalidationPatterns.Add(prefixedPattern);
        _store.Clear();
        return Task.CompletedTask;
    }
}
