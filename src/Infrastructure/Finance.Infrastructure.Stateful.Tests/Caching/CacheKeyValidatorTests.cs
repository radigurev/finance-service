using Finance.Common.ErrorCodes;
using Finance.Infrastructure.Caching;
using Finance.Infrastructure.Caching.Configuration;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Finance.Infrastructure.Stateful.Tests.Caching;

/// <summary>
/// Unit tests for <see cref="CacheKeyValidator"/> covering the registered-service-prefix rule for
/// both single keys and scan patterns (SDD-INFRA-004 §2.1, §3, §4): keys and patterns MUST start
/// with a registered <c>{service}:</c> prefix, otherwise a
/// <see cref="CacheKeyPatternViolationException"/> carrying
/// <see cref="CachingErrorCodes.CACHE_KEY_PATTERN_VIOLATION"/> is thrown.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-004")]
public sealed class CacheKeyValidatorTests
{
    private CacheKeyValidator _validator = null!;

    /// <summary>Builds a fresh validator with the default registered service prefixes.</summary>
    [SetUp]
    public void SetUp()
    {
        FinanceCacheOptions options = new();
        _validator = new CacheKeyValidator(Options.Create(options));
    }

    /// <summary>A key starting with a registered service prefix is accepted.</summary>
    [TestCase("finance-accounts:chart:all")]
    [TestCase("finance-currency:rates:byCode:EUR")]
    [TestCase("finance-periods:period:7")]
    [TestCase("finance-nomenclature:country:all")]
    public void ValidateKey_RegisteredPrefix_DoesNotThrow(string key)
    {
        // Arrange & Act & Assert
        Assert.That(() => _validator.ValidateKey(key), Throws.Nothing);
    }

    /// <summary>A key not prefixed by any registered service segment is rejected.</summary>
    [TestCase("warehouse-orders:order:1")]
    [TestCase("chart:all")]
    [TestCase("finance-accounts")]
    public void ValidateKey_UnregisteredPrefix_ThrowsPatternViolation(string key)
    {
        // Arrange & Act & Assert
        Assert.That(
            () => _validator.ValidateKey(key),
            Throws.TypeOf<CacheKeyPatternViolationException>());
    }

    /// <summary>An empty or whitespace key is rejected as a pattern violation.</summary>
    [TestCase("")]
    [TestCase("   ")]
    public void ValidateKey_EmptyOrWhitespace_ThrowsPatternViolation(string key)
    {
        // Arrange & Act & Assert
        Assert.That(
            () => _validator.ValidateKey(key),
            Throws.TypeOf<CacheKeyPatternViolationException>());
    }

    /// <summary>The thrown exception carries the CACHE_KEY_PATTERN_VIOLATION error code.</summary>
    [Test]
    public void ValidateKey_UnregisteredPrefix_ExceptionCarriesViolationCode()
    {
        // Arrange
        CacheKeyPatternViolationException? captured = null;

        // Act
        try
        {
            _validator.ValidateKey("not-finance:chart:all");
        }
        catch (CacheKeyPatternViolationException exception)
        {
            captured = exception;
        }

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.ErrorCode, Is.EqualTo(CachingErrorCodes.CACHE_KEY_PATTERN_VIOLATION));
        });
    }

    /// <summary>A scan pattern prefixed by a registered service segment is accepted.</summary>
    [Test]
    public void ValidatePattern_RegisteredPrefix_DoesNotThrow()
    {
        // Arrange & Act & Assert
        Assert.That(() => _validator.ValidatePattern("finance-accounts:chart:*"), Throws.Nothing);
    }

    /// <summary>An unbounded scan pattern without a registered service prefix is rejected (SDD-INFRA-004 §2.3).</summary>
    [TestCase("*")]
    [TestCase("chart:*")]
    [TestCase("warehouse-orders:*")]
    public void ValidatePattern_UnboundedOrUnregistered_ThrowsPatternViolation(string pattern)
    {
        // Arrange & Act & Assert
        Assert.That(
            () => _validator.ValidatePattern(pattern),
            Throws.TypeOf<CacheKeyPatternViolationException>());
    }

    /// <summary>A prefix-substring without the trailing colon does not count as a registered prefix.</summary>
    [Test]
    public void ValidateKey_PrefixWithoutColonSeparator_ThrowsPatternViolation()
    {
        // Arrange & Act & Assert
        Assert.That(
            () => _validator.ValidateKey("finance-accountsX:chart:all"),
            Throws.TypeOf<CacheKeyPatternViolationException>());
    }
}
