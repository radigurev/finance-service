using Finance.Infrastructure.Caching;
using Finance.Infrastructure.Caching.Configuration;
using Finance.Infrastructure.Caching.Interfaces;
using Finance.Infrastructure.Stateful.Tests.Caching.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using StackExchange.Redis;

namespace Finance.Infrastructure.Stateful.Tests.Caching;

/// <summary>
/// Unit tests for <see cref="RedisCacheService{T}"/> covering key validation, TTL-bounds enforcement,
/// and the fall-through-to-factory failure mode (SDD-INFRA-004 §2.5, §3, §6). Redis is simulated as
/// unreachable via a mocked <see cref="IConnectionMultiplexer"/>; no real Redis is required.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-004")]
public sealed class RedisCacheServiceTests
{
    private const string ValidKey = "finance-accounts:chart:all";

    private ICacheKeyValidator _keyValidator = null!;

    /// <summary>Builds a fresh key validator with the default registered prefixes before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _keyValidator = new CacheKeyValidator(Options.Create(new FinanceCacheOptions()));
    }

    /// <summary>When Redis is unreachable, GetOrSetAsync invokes the factory and returns its value without throwing.</summary>
    [Test]
    public async Task GetOrSetAsync_FallsThroughToFactory_WhenRedisDown()
    {
        // Arrange
        RedisCacheService<string> service = BuildServiceWithDownRedis();
        bool factoryInvoked = false;

        // Act
        string? result = await service.GetOrSetAsync(
            ValidKey,
            _ =>
            {
                factoryInvoked = true;
                return Task.FromResult<string?>("from-factory");
            },
            cancellationToken: CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(factoryInvoked, Is.True);
            Assert.That(result, Is.EqualTo("from-factory"));
        });
    }

    /// <summary>The fall-through path swallows the Redis failure rather than propagating it.</summary>
    [Test]
    public void GetOrSetAsync_WhenRedisDown_DoesNotThrow()
    {
        // Arrange
        RedisCacheService<string> service = BuildServiceWithDownRedis();

        // Act & Assert
        Assert.That(
            async () => await service.GetOrSetAsync(
                ValidKey,
                _ => Task.FromResult<string?>("value"),
                cancellationToken: CancellationToken.None),
            Throws.Nothing);
    }

    /// <summary>A key without a registered service prefix is rejected before any Redis call.</summary>
    [Test]
    public void GetOrSetAsync_KeyWithoutServicePrefix_ThrowsPatternViolation()
    {
        // Arrange
        RedisCacheService<string> service = BuildServiceWithDownRedis();

        // Act & Assert
        Assert.That(
            async () => await service.GetOrSetAsync(
                "unregistered:chart:all",
                _ => Task.FromResult<string?>("value"),
                cancellationToken: CancellationToken.None),
            Throws.TypeOf<CacheKeyPatternViolationException>());
    }

    /// <summary>A null factory is rejected with an ArgumentNullException.</summary>
    [Test]
    public void GetOrSetAsync_NullFactory_ThrowsArgumentNullException()
    {
        // Arrange
        RedisCacheService<string> service = BuildServiceWithDownRedis();

        // Act & Assert
        Assert.That(
            async () => await service.GetOrSetAsync(ValidKey, null!, cancellationToken: CancellationToken.None),
            Throws.TypeOf<ArgumentNullException>());
    }

    /// <summary>A TTL below the one-second minimum is rejected (SDD-INFRA-004 §3).</summary>
    [Test]
    public void GetOrSetAsync_TtlBelowMinimum_ThrowsArgumentOutOfRange()
    {
        // Arrange
        RedisCacheService<string> service = BuildServiceWithDownRedis();
        TimeSpan tooShort = TimeSpan.FromMilliseconds(500);

        // Act & Assert
        Assert.That(
            async () => await service.GetOrSetAsync(
                ValidKey,
                _ => Task.FromResult<string?>("value"),
                tooShort,
                CancellationToken.None),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    /// <summary>A TTL above the 24-hour maximum is rejected (SDD-INFRA-004 §3).</summary>
    [Test]
    public void GetOrSetAsync_TtlAboveMaximum_ThrowsArgumentOutOfRange()
    {
        // Arrange
        RedisCacheService<string> service = BuildServiceWithDownRedis();
        TimeSpan tooLong = TimeSpan.FromHours(25);

        // Act & Assert
        Assert.That(
            async () => await service.GetOrSetAsync(
                ValidKey,
                _ => Task.FromResult<string?>("value"),
                tooLong,
                CancellationToken.None),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    /// <summary>TTLs exactly at the inclusive bounds are accepted (factory still resolves on the down backend).</summary>
    [Test]
    public async Task GetOrSetAsync_TtlAtBounds_IsAccepted()
    {
        // Arrange
        RedisCacheService<string> service = BuildServiceWithDownRedis();

        // Act
        string? atMin = await service.GetOrSetAsync(
            ValidKey, _ => Task.FromResult<string?>("min"), CacheTtl.MinimumTtl, CancellationToken.None);
        string? atMax = await service.GetOrSetAsync(
            ValidKey, _ => Task.FromResult<string?>("max"), CacheTtl.MaximumTtl, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(atMin, Is.EqualTo("min"));
            Assert.That(atMax, Is.EqualTo("max"));
        });
    }

    /// <summary>RemoveAsync rejects a key without a registered service prefix.</summary>
    [Test]
    public void RemoveAsync_KeyWithoutServicePrefix_ThrowsPatternViolation()
    {
        // Arrange
        RedisCacheService<string> service = BuildServiceWithDownRedis();

        // Act & Assert
        Assert.That(
            async () => await service.RemoveAsync("unregistered:chart:all", CancellationToken.None),
            Throws.TypeOf<CacheKeyPatternViolationException>());
    }

    /// <summary>RemoveByPatternAsync rejects an unbounded pattern lacking a registered service prefix.</summary>
    [Test]
    public void RemoveByPatternAsync_UnboundedPattern_ThrowsPatternViolation()
    {
        // Arrange
        RedisCacheService<string> service = BuildServiceWithDownRedis();

        // Act & Assert
        Assert.That(
            async () => await service.RemoveByPatternAsync("*", CancellationToken.None),
            Throws.TypeOf<CacheKeyPatternViolationException>());
    }

    private RedisCacheService<string> BuildServiceWithDownRedis()
    {
        IConnectionMultiplexer multiplexer = RedisMultiplexerMocks.ThrowingOnEveryOperation();
        return new RedisCacheService<string>(
            multiplexer,
            _keyValidator,
            NullLogger<RedisCacheService<string>>.Instance);
    }
}
