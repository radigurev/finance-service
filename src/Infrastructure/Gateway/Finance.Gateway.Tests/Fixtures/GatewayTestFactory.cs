using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Finance.Gateway.Tests.Fixtures;

/// <summary>
/// Hosts <c>Finance.Gateway</c> in-process via <see cref="WebApplicationFactory{TEntryPoint}"/> with the
/// <c>ReverseProxy</c> and <c>HealthChecks</c> configuration pointed at in-process WireMock.Net stubs
/// (SDD-INFRA-002 §2.6). This makes the proxy, correlation, rate-limit, and health behavior exercisable
/// without any real downstream services or Docker.
/// </summary>
public sealed class GatewayTestFactory : WebApplicationFactory<Program>
{
    private readonly DownstreamStubs _stubs;

    /// <summary>Creates a factory whose gateway routes to the supplied downstream WireMock stubs.</summary>
    /// <param name="stubs">The in-process auth and accounts stand-ins the gateway proxies to.</param>
    public GatewayTestFactory(DownstreamStubs stubs)
    {
        _stubs = stubs;
    }

    /// <summary>
    /// Replaces the gateway configuration with an in-memory routing and health map that targets the
    /// WireMock stub base URLs, so the config-driven YARP gateway proxies to in-process stand-ins. Each
    /// key is pushed through <see cref="IWebHostBuilder.UseSetting"/> (host configuration) so it is visible
    /// to the gateway's eager <c>AddClusterReadinessHealthChecks</c> read, which runs during <c>Program</c>
    /// startup before <c>app.Run()</c> (SDD-INFRA-002 §2.6).
    /// </summary>
    /// <param name="builder">The web host builder for the in-process gateway.</param>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (KeyValuePair<string, string?> setting in BuildGatewayConfiguration())
        {
            builder.UseSetting(setting.Key, setting.Value);
        }
    }

    private Dictionary<string, string?> BuildGatewayConfiguration()
    {
        return new Dictionary<string, string?>
        {
            ["HealthChecks:AuthApi"] = _stubs.AuthBaseUrl,
            ["HealthChecks:AccountsApi"] = _stubs.AccountsBaseUrl,

            ["ReverseProxy:Routes:auth-route:ClusterId"] = "auth-cluster",
            ["ReverseProxy:Routes:auth-route:Match:Path"] = "/api/v1/auth/{**catch-all}",
            ["ReverseProxy:Routes:accounts-route:ClusterId"] = "accounts-cluster",
            ["ReverseProxy:Routes:accounts-route:Match:Path"] = "/api/v1/accounts/{**catch-all}",

            ["ReverseProxy:Clusters:auth-cluster:Destinations:auth-api:Address"] = _stubs.AuthBaseUrl,
            ["ReverseProxy:Clusters:accounts-cluster:Destinations:accounts-api:Address"] = _stubs.AccountsBaseUrl
        };
    }
}
