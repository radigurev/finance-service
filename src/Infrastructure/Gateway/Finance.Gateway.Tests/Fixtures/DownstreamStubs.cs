using System;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Finance.Gateway.Tests.Fixtures;

/// <summary>
/// Hosts in-process WireMock.Net servers that stand in for the downstream auth-service and
/// accounts-service (SDD-INFRA-002 §6). Because WireMock.Net needs NO Docker, the gateway proxy,
/// correlation, rate-limit, and health tests built on top of these stubs run in the default suite.
/// </summary>
public sealed class DownstreamStubs : IDisposable
{
    /// <summary>The path the gateway derives per cluster for readiness aggregation (SDD-INFRA-002 §2.4).</summary>
    public const string ReadyPath = "/health/ready";

    private readonly WireMockServer _authServer;
    private readonly WireMockServer _accountsServer;

    /// <summary>Starts both downstream stubs on ephemeral loopback ports with passing readiness by default.</summary>
    public DownstreamStubs()
    {
        _authServer = WireMockServer.Start();
        _accountsServer = WireMockServer.Start();

        StubReadiness(_authServer, healthy: true);
        StubReadiness(_accountsServer, healthy: true);
    }

    /// <summary>The base URL of the auth-service stub (maps to <c>auth-cluster</c>).</summary>
    public string AuthBaseUrl => _authServer.Url!;

    /// <summary>The base URL of the accounts-service stub (maps to <c>accounts-cluster</c>).</summary>
    public string AccountsBaseUrl => _accountsServer.Url!;

    /// <summary>The auth-service WireMock server, exposed so tests can add or inspect stubbed requests.</summary>
    public WireMockServer AuthServer => _authServer;

    /// <summary>The accounts-service WireMock server, exposed so tests can add or inspect stubbed requests.</summary>
    public WireMockServer AccountsServer => _accountsServer;

    /// <summary>Replaces the readiness stub on a server so a downstream <c>/health/ready</c> reports 503.</summary>
    /// <param name="server">The WireMock server whose readiness response should fail.</param>
    public static void MakeReadinessUnhealthy(WireMockServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        server.ResetMappings();
        StubReadiness(server, healthy: false);
    }

    /// <summary>Stops and disposes both downstream WireMock servers.</summary>
    public void Dispose()
    {
        _authServer.Stop();
        _authServer.Dispose();
        _accountsServer.Stop();
        _accountsServer.Dispose();
    }

    private static void StubReadiness(WireMockServer server, bool healthy)
    {
        server.Given(Request.Create().WithPath(ReadyPath).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(healthy ? 200 : 503));
    }
}
