using System.Net;
using System.Text.Json;
using Finance.Common.ErrorCodes;
using Finance.Infrastructure.Caching.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Finance.Infrastructure.Caching;

/// <summary>
/// Cache-aside <see cref="ICacheService{T}"/> over StackExchange.Redis
/// <see cref="IConnectionMultiplexer"/> using System.Text.Json serialization (SDD-INFRA-004 §2).
/// Validates keys and TTL bounds, and falls through to the factory while logging a warning
/// (<see cref="CachingErrorCodes.REDIS_UNREACHABLE"/>) when Redis is unreachable — it never throws
/// on connection failure (SDD-INFRA-004 §2.5).
/// </summary>
/// <typeparam name="T">The cached value type.</typeparam>
public sealed class RedisCacheService<T> : ICacheService<T>
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ICacheKeyValidator _keyValidator;
    private readonly ILogger<RedisCacheService<T>> _logger;
    private readonly JsonSerializerOptions _serializerOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisCacheService{T}"/> class.
    /// </summary>
    /// <param name="multiplexer">The shared Redis connection multiplexer.</param>
    /// <param name="keyValidator">Validator enforcing the registered service-prefix convention.</param>
    /// <param name="logger">Logger used for the fall-through warning on Redis failure.</param>
    public RedisCacheService(
        IConnectionMultiplexer multiplexer,
        ICacheKeyValidator keyValidator,
        ILogger<RedisCacheService<T>> logger)
    {
        ArgumentNullException.ThrowIfNull(multiplexer);
        ArgumentNullException.ThrowIfNull(keyValidator);
        ArgumentNullException.ThrowIfNull(logger);

        _multiplexer = multiplexer;
        _keyValidator = keyValidator;
        _logger = logger;
        _serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    /// <inheritdoc />
    public async Task<T?> GetOrSetAsync(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _keyValidator.ValidateKey(key);
        TimeSpan effectiveTtl = ResolveTtl(ttl);

        T? cached = await TryGetAsync(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        T? loaded = await factory(cancellationToken).ConfigureAwait(false);
        if (loaded is not null)
        {
            await TrySetAsync(key, loaded, effectiveTtl, cancellationToken).ConfigureAwait(false);
        }

        return loaded;
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _keyValidator.ValidateKey(key);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            IDatabase database = _multiplexer.GetDatabase();
            await database.KeyDeleteAsync(key).ConfigureAwait(false);
        }
        catch (RedisException exception)
        {
            LogRedisUnreachable(exception, nameof(RemoveAsync), key);
        }
        catch (RedisTimeoutException exception)
        {
            LogRedisUnreachable(exception, nameof(RemoveAsync), key);
        }
    }

    /// <inheritdoc />
    public async Task RemoveByPatternAsync(string prefixedPattern, CancellationToken cancellationToken = default)
    {
        _keyValidator.ValidatePattern(prefixedPattern);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await ScanAndDeleteAsync(prefixedPattern, cancellationToken).ConfigureAwait(false);
        }
        catch (RedisException exception)
        {
            LogRedisUnreachable(exception, nameof(RemoveByPatternAsync), prefixedPattern);
        }
        catch (RedisTimeoutException exception)
        {
            LogRedisUnreachable(exception, nameof(RemoveByPatternAsync), prefixedPattern);
        }
    }

    /// <summary>
    /// Runs a bounded multiplexer-level <c>SCAN</c> for <paramref name="prefixedPattern"/> across every
    /// server endpoint and deletes the matching keys (SDD-INFRA-004 §2.3).
    /// </summary>
    /// <param name="prefixedPattern">The service-prefixed glob pattern to scan for.</param>
    /// <param name="cancellationToken">Token used to cancel the scan loop.</param>
    private async Task ScanAndDeleteAsync(string prefixedPattern, CancellationToken cancellationToken)
    {
        IDatabase database = _multiplexer.GetDatabase();
        foreach (EndPoint endpoint in _multiplexer.GetEndPoints())
        {
            IServer server = _multiplexer.GetServer(endpoint);
            if (server.IsReplica)
            {
                continue;
            }

            await foreach (RedisKey redisKey in server
                .KeysAsync(pattern: prefixedPattern)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                await database.KeyDeleteAsync(redisKey).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Resolves and validates the effective TTL, applying the reference-data default when null.</summary>
    /// <param name="ttl">The caller-supplied TTL, or <c>null</c> to use the default.</param>
    /// <returns>The TTL to apply, guaranteed within bounds.</returns>
    private static TimeSpan ResolveTtl(TimeSpan? ttl)
    {
        TimeSpan effective = ttl ?? CacheTtl.Default;
        if (effective < CacheTtl.MinimumTtl || effective > CacheTtl.MaximumTtl)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ttl),
                effective,
                "Cache TTL must be within [1 second, 24 hours].");
        }

        return effective;
    }

    /// <summary>Attempts to read and deserialize a cached value, returning default on miss or failure.</summary>
    /// <param name="key">The validated cache key.</param>
    /// <param name="cancellationToken">Token observed before contacting Redis.</param>
    /// <returns>The cached value, or <c>default</c> on a miss or Redis failure.</returns>
    private async Task<T?> TryGetAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            IDatabase database = _multiplexer.GetDatabase();
            RedisValue value = await database.StringGetAsync(key).ConfigureAwait(false);
            if (value.IsNullOrEmpty)
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>((string)value!, _serializerOptions);
        }
        catch (RedisException exception)
        {
            LogRedisUnreachable(exception, nameof(GetOrSetAsync), key);
            return default;
        }
        catch (RedisTimeoutException exception)
        {
            LogRedisUnreachable(exception, nameof(GetOrSetAsync), key);
            return default;
        }
    }

    /// <summary>Attempts to serialize and store a value; swallows and logs Redis failures.</summary>
    /// <param name="key">The validated cache key.</param>
    /// <param name="value">The value to cache.</param>
    /// <param name="ttl">The expiry to apply.</param>
    /// <param name="cancellationToken">Token observed before contacting Redis.</param>
    private async Task TrySetAsync(string key, T value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            IDatabase database = _multiplexer.GetDatabase();
            string payload = JsonSerializer.Serialize(value, _serializerOptions);
            await database.StringSetAsync(key, payload, ttl).ConfigureAwait(false);
        }
        catch (RedisException exception)
        {
            LogRedisUnreachable(exception, nameof(GetOrSetAsync), key);
        }
        catch (RedisTimeoutException exception)
        {
            LogRedisUnreachable(exception, nameof(GetOrSetAsync), key);
        }
    }

    /// <summary>Logs a structured warning that Redis was unreachable and the caller fell through to the source.</summary>
    /// <param name="exception">The originating Redis failure.</param>
    /// <param name="operation">The cache operation that failed.</param>
    /// <param name="key">The key or pattern involved.</param>
    private void LogRedisUnreachable(Exception exception, string operation, string key)
    {
        _logger.LogWarning(
            exception,
            "Redis unreachable during {Operation} for key {CacheKey}; falling through. Code={ErrorCode}",
            operation,
            key,
            CachingErrorCodes.REDIS_UNREACHABLE);
    }
}
