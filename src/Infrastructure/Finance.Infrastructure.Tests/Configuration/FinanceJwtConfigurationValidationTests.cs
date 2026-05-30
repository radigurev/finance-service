using Finance.Infrastructure.Web.Configuration;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Finance.Infrastructure.Tests.Configuration;

/// <summary>
/// Unit tests for <see cref="ConfigurationValidation.ValidateFinanceJwtConfiguration"/> covering the
/// SDD-INT-AUTH-001 §3.1 Finance-owned JWT-config fail-fast rules: a missing or short <c>Jwt:SecretKey</c>
/// and an empty <c>Jwt:Issuer</c>/<c>Jwt:Audience</c> each abort startup, while a complete, valid
/// configuration passes without throwing. RBAC tests that need a real auth-service permission lookup
/// (401/403 paths) require external infrastructure and are tracked under <c>[Category("Integration")]</c>.
/// </summary>
[TestFixture]
[Category("SDD-INT-AUTH-001")]
public sealed class FinanceJwtConfigurationValidationTests
{
    private const string ValidSecretKey = "this-is-a-sufficiently-long-secret-key-1234567890";
    private const string ValidIssuer = "https://auth.local";
    private const string ValidAudience = "warehouse-platform";

    /// <summary>A missing Jwt:SecretKey aborts startup with a key-naming InvalidOperationException (§3.1).</summary>
    [Test]
    public void ValidateFinanceJwtConfiguration_Throws_WhenSecretKeyMissing()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(
            ("Jwt:Issuer", ValidIssuer),
            ("Jwt:Audience", ValidAudience));

        // Act & Assert
        Assert.That(
            () => ConfigurationValidation.ValidateFinanceJwtConfiguration(configuration),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("Jwt:SecretKey"));
    }

    /// <summary>An empty or whitespace Jwt:SecretKey is treated as missing and aborts startup (§3.1).</summary>
    [TestCase("")]
    [TestCase("   ")]
    public void ValidateFinanceJwtConfiguration_Throws_WhenSecretKeyEmptyOrWhitespace(string secretKey)
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(
            ("Jwt:SecretKey", secretKey),
            ("Jwt:Issuer", ValidIssuer),
            ("Jwt:Audience", ValidAudience));

        // Act & Assert
        Assert.That(
            () => ConfigurationValidation.ValidateFinanceJwtConfiguration(configuration),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("Jwt:SecretKey"));
    }

    /// <summary>A Jwt:SecretKey shorter than 32 characters aborts startup naming the key (§3.1).</summary>
    [Test]
    public void ValidateFinanceJwtConfiguration_Throws_WhenSecretKeyShorterThan32Chars()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(
            ("Jwt:SecretKey", "short-key-31-characters-long-xx"),
            ("Jwt:Issuer", ValidIssuer),
            ("Jwt:Audience", ValidAudience));

        // Act & Assert
        Assert.That(
            () => ConfigurationValidation.ValidateFinanceJwtConfiguration(configuration),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("Jwt:SecretKey"));
    }

    /// <summary>An empty or whitespace Jwt:Issuer aborts startup naming the key (§3.1).</summary>
    [TestCase("")]
    [TestCase("   ")]
    public void ValidateFinanceJwtConfiguration_Throws_WhenIssuerEmpty(string issuer)
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(
            ("Jwt:SecretKey", ValidSecretKey),
            ("Jwt:Issuer", issuer),
            ("Jwt:Audience", ValidAudience));

        // Act & Assert
        Assert.That(
            () => ConfigurationValidation.ValidateFinanceJwtConfiguration(configuration),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("Jwt:Issuer"));
    }

    /// <summary>An empty or whitespace Jwt:Audience aborts startup naming the key (§3.1).</summary>
    [TestCase("")]
    [TestCase("   ")]
    public void ValidateFinanceJwtConfiguration_Throws_WhenAudienceEmpty(string audience)
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(
            ("Jwt:SecretKey", ValidSecretKey),
            ("Jwt:Issuer", ValidIssuer),
            ("Jwt:Audience", audience));

        // Act & Assert
        Assert.That(
            () => ConfigurationValidation.ValidateFinanceJwtConfiguration(configuration),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("Jwt:Audience"));
    }

    /// <summary>A SecretKey of exactly 32 characters with valid issuer/audience passes (boundary, §3.1).</summary>
    [Test]
    public void ValidateFinanceJwtConfiguration_Succeeds_WhenSecretKeyExactly32Chars()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(
            ("Jwt:SecretKey", new string('k', 32)),
            ("Jwt:Issuer", ValidIssuer),
            ("Jwt:Audience", ValidAudience));

        // Act & Assert
        Assert.That(
            () => ConfigurationValidation.ValidateFinanceJwtConfiguration(configuration),
            Throws.Nothing);
    }

    /// <summary>A complete, valid JWT configuration completes without throwing (§3.1).</summary>
    [Test]
    public void ValidateFinanceJwtConfiguration_Succeeds_WhenAllValuesPresentAndValid()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(
            ("Jwt:SecretKey", ValidSecretKey),
            ("Jwt:Issuer", ValidIssuer),
            ("Jwt:Audience", ValidAudience));

        // Act & Assert
        Assert.That(
            () => ConfigurationValidation.ValidateFinanceJwtConfiguration(configuration),
            Throws.Nothing);
    }

    /// <summary>A null configuration argument is rejected with an ArgumentNullException.</summary>
    [Test]
    public void ValidateFinanceJwtConfiguration_NullConfiguration_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.That(
            () => ConfigurationValidation.ValidateFinanceJwtConfiguration(null!),
            Throws.TypeOf<ArgumentNullException>());
    }

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] entries)
    {
        Dictionary<string, string?> store = new();
        foreach ((string key, string value) in entries)
        {
            store[key] = value;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(store)
            .Build();
    }
}
