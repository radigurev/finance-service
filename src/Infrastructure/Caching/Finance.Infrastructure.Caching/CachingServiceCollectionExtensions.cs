using Finance.Common.ErrorCodes;
using Finance.Infrastructure.Caching.Configuration;
using Finance.Infrastructure.Caching.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Finance.Infrastructure.Caching;

/// <summary>
/// Dependency-injection registration for the Finance Redis cache layer (SDD-INFRA-004). This bundle
/// OWNS the lazy <see cref="IConnectionMultiplexer"/> registration that
/// <c>Finance.Infrastructure.Messaging</c> (SDD-INFRA-006) reuses for its idempotency filter.
/// </summary>
public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the lazy Redis <see cref="IConnectionMultiplexer"/>, the cache-key validator, and the
    /// open-generic <see cref="ICacheService{T}"/>. Validates at registration time that
    /// <c>ConnectionStrings:Redis</c> is present (SDD-INFRA-004 §3).
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">The application configuration carrying <c>ConnectionStrings:Redis</c>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddFinanceRedisCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = RequireRedisConnectionString(configuration);

        services.Configure<FinanceCacheOptions>(options => options.ConnectionString = connectionString);
        services.AddSingleton<IConnectionMultiplexer>(_ => CreateLazyMultiplexer(connectionString));
        services.AddSingleton<ICacheKeyValidator, CacheKeyValidator>();
        services.AddSingleton(typeof(ICacheService<>), typeof(RedisCacheService<>));

        return services;
    }

    /// <summary>
    /// Resolves and validates the Redis connection string from <c>ConnectionStrings:Redis</c>,
    /// throwing an <see cref="InvalidOperationException"/> when it is missing (SDD-INFRA-004 §3).
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The validated Redis connection string.</returns>
    private static string RequireRedisConnectionString(IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:Redis is required for the Finance cache layer. "
                + $"Code={CachingErrorCodes.REDIS_UNREACHABLE}.");
        }

        return connectionString;
    }

    /// <summary>
    /// Connects the StackExchange.Redis multiplexer with abort-on-connect disabled so the service
    /// starts even when Redis is temporarily down and the cache falls through (SDD-INFRA-004 §2.5).
    /// </summary>
    /// <param name="connectionString">The validated Redis connection string.</param>
    /// <returns>A connected <see cref="IConnectionMultiplexer"/>.</returns>
    private static IConnectionMultiplexer CreateLazyMultiplexer(string connectionString)
    {
        ConfigurationOptions options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        return ConnectionMultiplexer.Connect(options);
    }
}
