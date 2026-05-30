using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Finance.Gateway.Tests.Validation;

/// <summary>
/// Unit tests for <see cref="GatewayConfigurationValidator"/> covering the SDD-INFRA-002 §3.1 fail-fast
/// startup rules: a cluster with no destination and a non-absolute <c>HealthChecks:*</c> URI must each
/// abort startup with a clear, offending-key-naming <see cref="InvalidOperationException"/>.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-002")]
public sealed class GatewayConfigurationValidatorTests
{
    private const string ValidAccountsUrl = "http://accounts:5001";
    private const string ValidAuthUrl = "http://auth:5000";

    /// <summary>A cluster declaring no destination aborts startup, naming the offending cluster (§3.1).</summary>
    [Test]
    public void Gateway_Startup_Fails_WhenClusterHasNoDestination()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(
            ("HealthChecks:AccountsApi", ValidAccountsUrl),
            ("ReverseProxy:Clusters:accounts-cluster:Destinations:placeholder:NotAnAddress", "x"));

        // Act & Assert
        Assert.That(
            () => GatewayConfigurationValidator.Validate(configuration),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("accounts-cluster"));
    }

    /// <summary>A non-absolute HealthChecks URI aborts startup, naming the offending key (§3.1).</summary>
    [Test]
    public void Gateway_Startup_Fails_WhenHealthCheckUriNotAbsolute()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(
            ("HealthChecks:AccountsApi", "not-a-uri"),
            ("ReverseProxy:Clusters:accounts-cluster:Destinations:accounts-api:Address", ValidAccountsUrl));

        // Act & Assert
        Assert.That(
            () => GatewayConfigurationValidator.Validate(configuration),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("HealthChecks:AccountsApi"));
    }

    /// <summary>An empty HealthChecks URI aborts startup, naming the offending key (§3.1).</summary>
    [Test]
    public void Validate_HealthCheckUriEmpty_ThrowsNamingTheKey()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(
            ("HealthChecks:AuthApi", string.Empty),
            ("ReverseProxy:Clusters:auth-cluster:Destinations:auth-api:Address", ValidAuthUrl));

        // Act & Assert
        Assert.That(
            () => GatewayConfigurationValidator.Validate(configuration),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("HealthChecks:AuthApi"));
    }

    /// <summary>A cluster destination address that is not an absolute URI aborts startup (§3.1).</summary>
    [Test]
    public void Validate_ClusterDestinationNotAbsolute_ThrowsNamingTheCluster()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(
            ("HealthChecks:AccountsApi", ValidAccountsUrl),
            ("ReverseProxy:Clusters:accounts-cluster:Destinations:accounts-api:Address", "relative/path"));

        // Act & Assert
        Assert.That(
            () => GatewayConfigurationValidator.Validate(configuration),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("accounts-cluster"));
    }

    /// <summary>A fully valid configuration with absolute URIs and a destination per cluster passes.</summary>
    [Test]
    public void Validate_ValidConfiguration_DoesNotThrow()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(
            ("HealthChecks:AuthApi", ValidAuthUrl),
            ("HealthChecks:AccountsApi", ValidAccountsUrl),
            ("ReverseProxy:Clusters:auth-cluster:Destinations:auth-api:Address", ValidAuthUrl),
            ("ReverseProxy:Clusters:accounts-cluster:Destinations:accounts-api:Address", ValidAccountsUrl));

        // Act & Assert
        Assert.That(
            () => GatewayConfigurationValidator.Validate(configuration),
            Throws.Nothing);
    }

    /// <summary>A null configuration argument is rejected with an ArgumentNullException.</summary>
    [Test]
    public void Validate_NullConfiguration_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.That(
            () => GatewayConfigurationValidator.Validate(null!),
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
