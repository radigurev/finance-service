using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.Gateway;

/// <summary>
/// Derives the gateway's readiness health checks from the <c>ReverseProxy:Clusters</c> configuration
/// (SDD-INFRA-002 §2.4): for each configured cluster it registers a URL-group check against that cluster's
/// first destination plus <c>/health/ready</c>, tagged <c>ready</c>, so new clusters are covered automatically.
/// <para>Relies on <see cref="GatewayConfigurationValidator"/> having validated the configuration first.</para>
/// </summary>
public static class GatewayHealthCheckRegistration
{
    private const string ClustersSection = "ReverseProxy:Clusters";
    private const string ReadyPath = "/health/ready";
    private const string ReadyTag = "ready";

    /// <summary>
    /// Registers one readiness URL-group check per configured cluster, targeting the cluster's first
    /// destination address joined with <c>/health/ready</c>.
    /// </summary>
    /// <param name="services">The gateway service collection.</param>
    /// <param name="configuration">The gateway application configuration.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddClusterReadinessHealthChecks(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        IHealthChecksBuilder builder = services.AddHealthChecks();

        foreach (ClusterReadinessCheck check in DeriveReadinessChecks(configuration))
        {
            builder.AddUrlGroup(check.ReadyUri, name: check.Name, tags: [ReadyTag]);
        }

        return services;
    }

    /// <summary>
    /// Derives the readiness check descriptor for each configured cluster from the first destination address.
    /// </summary>
    /// <param name="configuration">The gateway application configuration.</param>
    /// <returns>The readiness checks, one per cluster declaring a valid first destination.</returns>
    public static IReadOnlyList<ClusterReadinessCheck> DeriveReadinessChecks(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        List<ClusterReadinessCheck> checks = [];
        foreach (IConfigurationSection cluster in configuration.GetSection(ClustersSection).GetChildren())
        {
            string address = GatewayConfigurationValidator.ReadFirstDestinationAddress(cluster);
            if (!GatewayConfigurationValidator.IsAbsoluteUri(address))
            {
                continue;
            }

            Uri readyUri = new(new Uri(address, UriKind.Absolute), ReadyPath);
            checks.Add(new ClusterReadinessCheck(cluster.Key, readyUri));
        }

        return checks;
    }
}
