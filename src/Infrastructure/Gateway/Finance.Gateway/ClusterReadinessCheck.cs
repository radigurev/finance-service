namespace Finance.Gateway;

/// <summary>
/// Describes a readiness health check derived from a YARP cluster (SDD-INFRA-002 §2.4): the cluster name
/// and the fully-resolved <c>/health/ready</c> URI of that cluster's first destination.
/// <para>Produced by <see cref="GatewayHealthCheckRegistration.DeriveReadinessChecks"/>.</para>
/// </summary>
/// <param name="ClusterId">The YARP cluster identifier the check targets.</param>
/// <param name="ReadyUri">The absolute readiness URI (first destination address + <c>/health/ready</c>).</param>
public sealed record ClusterReadinessCheck(string ClusterId, Uri ReadyUri)
{
    /// <summary>The health-check registration name, suffixed so it reads clearly in the aggregated report.</summary>
    public string Name => $"{ClusterId}-ready";
}
