using Microsoft.Extensions.Configuration;

namespace Finance.Gateway;

/// <summary>
/// Fail-fast startup validation for the gateway's routing and health configuration (SDD-INFRA-002 §3.1).
/// Verifies every <c>HealthChecks:*</c> value is an absolute URI and every <c>ReverseProxy:Clusters</c>
/// cluster declares at least one destination, aborting startup with a clear message otherwise.
/// <para>Consumed by <see cref="GatewayHealthCheckRegistration"/> and the gateway composition root.</para>
/// </summary>
public static class GatewayConfigurationValidator
{
    private const string HealthChecksSection = "HealthChecks";
    private const string ClustersSection = "ReverseProxy:Clusters";

    /// <summary>
    /// Validates the gateway configuration and throws an <see cref="InvalidOperationException"/> with an
    /// actionable message when a <c>HealthChecks:*</c> value is not an absolute URI or a cluster has no destination.
    /// </summary>
    /// <param name="configuration">The gateway application configuration.</param>
    /// <exception cref="InvalidOperationException">When a health URI is invalid or a cluster has no destination.</exception>
    public static void Validate(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        ValidateHealthCheckUris(configuration);
        ValidateClusterDestinations(configuration);
    }

    /// <summary>Ensures every configured <c>HealthChecks:*</c> value parses as an absolute URI.</summary>
    /// <param name="configuration">The gateway application configuration.</param>
    private static void ValidateHealthCheckUris(IConfiguration configuration)
    {
        foreach (IConfigurationSection entry in configuration.GetSection(HealthChecksSection).GetChildren())
        {
            string key = $"{HealthChecksSection}:{entry.Key}";
            if (!IsAbsoluteUri(entry.Value))
            {
                throw new InvalidOperationException(
                    $"Gateway configuration key '{key}' must be a valid absolute URI but was '{entry.Value}'. " +
                    "Set it to a full address such as 'http://host:port'.");
            }
        }
    }

    /// <summary>Ensures every cluster under <c>ReverseProxy:Clusters</c> declares at least one destination address.</summary>
    /// <param name="configuration">The gateway application configuration.</param>
    private static void ValidateClusterDestinations(IConfiguration configuration)
    {
        foreach (IConfigurationSection cluster in configuration.GetSection(ClustersSection).GetChildren())
        {
            string firstAddress = ReadFirstDestinationAddress(cluster);
            if (string.IsNullOrWhiteSpace(firstAddress))
            {
                throw new InvalidOperationException(
                    $"Gateway cluster '{cluster.Key}' under '{ClustersSection}' must declare at least one " +
                    "destination with an Address. Add a Destinations entry before starting the gateway.");
            }

            if (!IsAbsoluteUri(firstAddress))
            {
                throw new InvalidOperationException(
                    $"Gateway cluster '{cluster.Key}' first destination address '{firstAddress}' is not a valid " +
                    "absolute URI. Set it to a full address such as 'http://host:port'.");
            }
        }
    }

    /// <summary>Reads the address of the first destination declared under the given cluster section.</summary>
    /// <param name="cluster">The cluster configuration section.</param>
    /// <returns>The first destination address, or an empty string when none is declared.</returns>
    internal static string ReadFirstDestinationAddress(IConfigurationSection cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);

        IConfigurationSection? firstDestination = cluster
            .GetSection("Destinations")
            .GetChildren()
            .FirstOrDefault();

        return firstDestination?["Address"] ?? string.Empty;
    }

    /// <summary>Determines whether the supplied value is a non-empty, absolute URI.</summary>
    /// <param name="value">The candidate URI string.</param>
    /// <returns><c>true</c> when the value parses as an absolute URI; otherwise <c>false</c>.</returns>
    internal static bool IsAbsoluteUri(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Uri.TryCreate(value, UriKind.Absolute, out _);
}
