using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Finance.Gateway.Tests.Fixtures;
using NUnit.Framework;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Finance.Gateway.Tests.Proxy;

/// <summary>
/// In-process gateway behavior tests (SDD-INFRA-002 §2). The gateway is hosted via
/// <c>WebApplicationFactory&lt;Program&gt;</c> with the <c>ReverseProxy</c>/<c>HealthChecks</c> config pointed
/// at in-process WireMock.Net stubs, so routing, correlation propagation, rate limiting, and health
/// aggregation are verified without Docker or real downstream services. These tests run in the default
/// suite (they are NOT marked <c>[Category("Integration")]</c>) because WireMock.Net needs no external infra.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-002")]
public sealed class GatewayProxyTests
{
    private const string AccountsPath = "/api/v1/accounts";
    private const string AuthPath = "/api/v1/auth/login";
    private const string CorrelationHeader = "X-Correlation-ID";

    private DownstreamStubs _stubs = null!;
    private GatewayTestFactory _factory = null!;

    /// <summary>Starts fresh downstream stubs and an in-process gateway before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _stubs = new DownstreamStubs();
        _factory = new GatewayTestFactory(_stubs);
    }

    /// <summary>Disposes the in-process gateway and downstream stubs after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
        _stubs.Dispose();
    }

    /// <summary>A request to the accounts route is proxied to the accounts stub and its response returned.</summary>
    [Test]
    public async Task Gateway_ProxiesAccountsRouteToAccountsApi()
    {
        // Arrange
        _stubs.AccountsServer
            .Given(Request.Create().WithPath(AccountsPath).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("[{\"code\":\"1000\"}]"));
        HttpClient client = _factory.CreateClient();

        // Act
        HttpResponseMessage response = await client.GetAsync(AccountsPath, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        string body = await response.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("1000"));
        });
    }

    /// <summary>A request to an auth route is proxied to the auth stub via the auth-cluster.</summary>
    [Test]
    public async Task Gateway_ProxiesAuthRouteToAuthService()
    {
        // Arrange
        _stubs.AuthServer
            .Given(Request.Create().WithPath(AuthPath).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"token\":\"jwt\"}"));
        HttpClient client = _factory.CreateClient();

        // Act
        HttpResponseMessage response = await client
            .PostAsync(AuthPath, new StringContent(string.Empty), CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        string body = await response.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("jwt"));
        });
    }

    /// <summary>The gateway copies the inbound X-Correlation-ID onto the outbound proxy request (§2.2).</summary>
    [Test]
    public async Task Gateway_AddsCorrelationIdHeaderToOutboundProxyRequest()
    {
        // Arrange
        _stubs.AccountsServer
            .Given(Request.Create().WithPath(AccountsPath).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200));
        HttpClient client = _factory.CreateClient();
        string correlationId = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add(CorrelationHeader, correlationId);

        // Act
        await client.GetAsync(AccountsPath, CancellationToken.None).ConfigureAwait(false);

        // Assert
        IReadOnlyList<string> received = _stubs.AccountsServer.LogEntries
            .Where(entry => entry.RequestMessage.Headers is not null)
            .SelectMany(entry => entry.RequestMessage.Headers!)
            .Where(header => string.Equals(header.Key, CorrelationHeader, StringComparison.OrdinalIgnoreCase))
            .SelectMany(header => header.Value)
            .ToList();
        Assert.That(received, Does.Contain(correlationId));
    }

    /// <summary>Flooding past the 200/min global per-IP limit makes the gateway return HTTP 429 (§2.3).</summary>
    [Test]
    public async Task Gateway_ReturnsRateLimited_WhenIpExceedsGlobalLimit()
    {
        // Arrange
        _stubs.AccountsServer
            .Given(Request.Create().WithPath(AccountsPath).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromMilliseconds(200)));
        HttpClient client = _factory.CreateClient();

        // Act
        List<Task<HttpStatusCode>> attempts = [];
        for (int attempt = 0; attempt < 400; attempt++)
        {
            attempts.Add(SendAndReadStatusAsync(client, AccountsPath));
        }
        bool sawRateLimited = await AnyStatusEqualsAsync(attempts, StatusCodes429).ConfigureAwait(false);

        // Assert
        Assert.That(sawRateLimited, Is.True);
    }

    private static async Task<HttpStatusCode> SendAndReadStatusAsync(HttpClient client, string path)
    {
        using HttpResponseMessage response = await client.GetAsync(path, CancellationToken.None)
            .ConfigureAwait(false);
        return response.StatusCode;
    }

    private static async Task<bool> AnyStatusEqualsAsync(List<Task<HttpStatusCode>> attempts, int target)
    {
        List<Task<HttpStatusCode>> pending = [.. attempts];
        while (pending.Count > 0)
        {
            Task<HttpStatusCode> completed = await Task.WhenAny(pending).ConfigureAwait(false);
            pending.Remove(completed);
            if ((int)completed.Result == target)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>When a downstream readiness check returns 503, the gateway's /health returns 503 (§2.4).</summary>
    [Test]
    public async Task Gateway_HealthEndpoint_Returns503_WhenDownstreamReadyFails()
    {
        // Arrange
        DownstreamStubs.MakeReadinessUnhealthy(_stubs.AccountsServer);
        HttpClient client = _factory.CreateClient();

        // Act
        HttpResponseMessage response = await client.GetAsync("/health", CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
    }

    /// <summary>When every derived cluster readiness check passes, the gateway's /health returns 200 (§2.4).</summary>
    [Test]
    public async Task Gateway_HealthEndpoint_Returns200_WhenAllDerivedClusterReadyChecksPass()
    {
        // Arrange
        HttpClient client = _factory.CreateClient();

        // Act
        HttpResponseMessage response = await client.GetAsync("/health", CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private const int StatusCodes429 = 429;
}
