using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Finance.Gateway.Tests.Validation;

/// <summary>
/// Unit tests for <see cref="GatewayHealthCheckRegistration.DeriveReadinessChecks"/> covering the
/// SDD-INFRA-002 §2.4 rule that the readiness check set is DERIVED from <c>ReverseProxy:Clusters</c> —
/// one check per cluster, targeting that cluster's first destination address plus <c>/health/ready</c> —
/// so adding a new cluster extends health aggregation without code changes.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-002")]
public sealed class GatewayHealthCheckRegistrationTests
{
    /// <summary>Each configured cluster yields exactly one readiness check named after the cluster (§2.4).</summary>
    [Test]
    public void Gateway_HealthAggregation_DerivesReadyCheckPerConfiguredCluster()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(
            ("ReverseProxy:Clusters:auth-cluster:Destinations:auth-api:Address", "http://auth:5000"),
            ("ReverseProxy:Clusters:accounts-cluster:Destinations:accounts-api:Address", "http://accounts:5001"),
            ("ReverseProxy:Clusters:eventlog-cluster:Destinations:eventlog-api:Address", "http://eventlog:5003"));

        // Act
        IReadOnlyList<ClusterReadinessCheck> checks =
            GatewayHealthCheckRegistration.DeriveReadinessChecks(configuration);

        // Assert
        IReadOnlyList<string> names = checks.Select(check => check.Name).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(checks, Has.Count.EqualTo(3));
            Assert.That(names, Does.Contain("auth-cluster-ready"));
            Assert.That(names, Does.Contain("accounts-cluster-ready"));
            Assert.That(names, Does.Contain("eventlog-cluster-ready"));
        });
    }

    /// <summary>The derived readiness URI is the first destination address joined with /health/ready (§2.4).</summary>
    [Test]
    public void DeriveReadinessChecks_AppendsHealthReadyPathToFirstDestination()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(
            ("ReverseProxy:Clusters:accounts-cluster:Destinations:accounts-api:Address", "http://accounts:5001"));

        // Act
        ClusterReadinessCheck check = GatewayHealthCheckRegistration.DeriveReadinessChecks(configuration).Single();

        // Assert
        Assert.That(check.ReadyUri, Is.EqualTo(new Uri("http://accounts:5001/health/ready")));
    }

    /// <summary>A newly added cluster is automatically included without any code change (§2.4).</summary>
    [Test]
    public void DeriveReadinessChecks_NewClusterIsCoveredAutomatically()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(
            ("ReverseProxy:Clusters:auth-cluster:Destinations:auth-api:Address", "http://auth:5000"),
            ("ReverseProxy:Clusters:journal-cluster:Destinations:journal-api:Address", "http://journal:5010"));

        // Act
        IReadOnlyList<ClusterReadinessCheck> checks =
            GatewayHealthCheckRegistration.DeriveReadinessChecks(configuration);

        // Assert
        Assert.That(checks.Select(check => check.ClusterId), Does.Contain("journal-cluster"));
    }

    /// <summary>A cluster whose first destination address is not absolute is skipped, not derived (§2.4).</summary>
    [Test]
    public void DeriveReadinessChecks_ClusterWithNonAbsoluteAddress_IsSkipped()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(
            ("ReverseProxy:Clusters:auth-cluster:Destinations:auth-api:Address", "http://auth:5000"),
            ("ReverseProxy:Clusters:broken-cluster:Destinations:broken-api:Address", "relative/only"));

        // Act
        IReadOnlyList<ClusterReadinessCheck> checks =
            GatewayHealthCheckRegistration.DeriveReadinessChecks(configuration);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(checks, Has.Count.EqualTo(1));
            Assert.That(checks.Single().ClusterId, Is.EqualTo("auth-cluster"));
        });
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
