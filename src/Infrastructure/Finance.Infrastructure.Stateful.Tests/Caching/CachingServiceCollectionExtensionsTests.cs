using Finance.Infrastructure.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Finance.Infrastructure.Stateful.Tests.Caching;

/// <summary>
/// Unit tests for the startup fail-fast guard of
/// <see cref="CachingServiceCollectionExtensions.AddFinanceRedisCache(IServiceCollection, IConfiguration)"/>
/// (SDD-INFRA-004 §3). Registration MUST throw <see cref="InvalidOperationException"/> when
/// <c>ConnectionStrings:Redis</c> is missing, before any multiplexer is created. With the key present the
/// factory registration is deferred, so registration itself MUST NOT throw or connect to Redis.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-004")]
public sealed class CachingServiceCollectionExtensionsTests
{
    /// <summary>Missing ConnectionStrings:Redis fails fast at registration with InvalidOperationException.</summary>
    [Test]
    public void AddFinanceRedisCache_MissingRedisConnectionString_ThrowsInvalidOperationException()
    {
        // Arrange
        ServiceCollection services = new();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        // Act & Assert
        Assert.That(
            () => services.AddFinanceRedisCache(configuration),
            Throws.TypeOf<InvalidOperationException>());
    }

    /// <summary>A whitespace-only ConnectionStrings:Redis is treated as missing and fails fast.</summary>
    [Test]
    public void AddFinanceRedisCache_WhitespaceRedisConnectionString_ThrowsInvalidOperationException()
    {
        // Arrange
        ServiceCollection services = new();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "   "
            })
            .Build();

        // Act & Assert
        Assert.That(
            () => services.AddFinanceRedisCache(configuration),
            Throws.TypeOf<InvalidOperationException>());
    }

    /// <summary>With ConnectionStrings:Redis present, registration completes without throwing or connecting.</summary>
    [Test]
    public void AddFinanceRedisCache_WithRedisConnectionString_DoesNotThrow()
    {
        // Arrange
        ServiceCollection services = new();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "localhost:6379,abortConnect=false"
            })
            .Build();

        // Act & Assert
        Assert.That(
            () => services.AddFinanceRedisCache(configuration),
            Throws.Nothing);
    }
}
