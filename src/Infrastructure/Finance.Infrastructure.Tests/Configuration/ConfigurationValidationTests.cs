using Finance.Infrastructure.Web.Configuration;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Finance.Infrastructure.Tests.Configuration;

/// <summary>
/// Unit tests for <see cref="ConfigurationValidation.EnsureRequiredConfiguration"/> covering the
/// SDD-INFRA-001 §3 startup fail-fast rule: a missing or empty required key throws a clear,
/// key-naming exception while an all-present configuration passes.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-001")]
public sealed class ConfigurationValidationTests
{
    /// <summary>A configuration with every required key present and non-empty does not throw.</summary>
    [Test]
    public void EnsureRequiredConfiguration_AllKeysPresent_DoesNotThrow()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(
            ("ConnectionStrings:FinanceAccountsDb", "Server=.;Database=FinanceAccounts;"),
            ("Jwt:Authority", "https://auth.local"));

        // Act & Assert
        Assert.That(
            () => ConfigurationValidation.EnsureRequiredConfiguration(
                configuration, "ConnectionStrings:FinanceAccountsDb", "Jwt:Authority"),
            Throws.Nothing);
    }

    /// <summary>An empty required-keys list never throws regardless of configuration content.</summary>
    [Test]
    public void EnsureRequiredConfiguration_NoRequiredKeys_DoesNotThrow()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration();

        // Act & Assert
        Assert.That(
            () => ConfigurationValidation.EnsureRequiredConfiguration(configuration),
            Throws.Nothing);
    }

    /// <summary>A required key that is entirely absent throws an InvalidOperationException naming the key.</summary>
    [Test]
    public void EnsureRequiredConfiguration_MissingKey_ThrowsNamingTheKey()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(("Jwt:Authority", "https://auth.local"));

        // Act & Assert
        Assert.That(
            () => ConfigurationValidation.EnsureRequiredConfiguration(
                configuration, "ConnectionStrings:FinanceAccountsDb"),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("ConnectionStrings:FinanceAccountsDb"));
    }

    /// <summary>A required key present but empty or whitespace is treated as missing and throws.</summary>
    [TestCase("")]
    [TestCase("   ")]
    public void EnsureRequiredConfiguration_EmptyOrWhitespaceValue_ThrowsNamingTheKey(string value)
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(("ConnectionStrings:FinanceAccountsDb", value));

        // Act & Assert
        Assert.That(
            () => ConfigurationValidation.EnsureRequiredConfiguration(
                configuration, "ConnectionStrings:FinanceAccountsDb"),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("ConnectionStrings:FinanceAccountsDb"));
    }

    /// <summary>The first missing key is the one named when several keys are required.</summary>
    [Test]
    public void EnsureRequiredConfiguration_FirstMissingKey_IsNamedInMessage()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(
            ("ConnectionStrings:FinanceAccountsDb", "Server=.;Database=FinanceAccounts;"));

        // Act & Assert
        Assert.That(
            () => ConfigurationValidation.EnsureRequiredConfiguration(
                configuration, "ConnectionStrings:FinanceAccountsDb", "Jwt:Authority"),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("Jwt:Authority"));
    }

    /// <summary>A null configuration argument is rejected with an ArgumentNullException.</summary>
    [Test]
    public void EnsureRequiredConfiguration_NullConfiguration_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.That(
            () => ConfigurationValidation.EnsureRequiredConfiguration(null!, "AnyKey"),
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
