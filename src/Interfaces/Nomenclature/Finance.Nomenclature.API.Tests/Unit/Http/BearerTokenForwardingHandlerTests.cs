using System.Net;
using System.Net.Http.Headers;
using Finance.Nomenclature.API.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Moq;
using NUnit.Framework;

namespace Finance.Nomenclature.API.Tests.Unit.Http;

/// <summary>
/// Unit tests for <see cref="BearerTokenForwardingHandler"/> (SDD-NOM-001 §2.3). Service-to-service JWT is
/// deferred, so the handler forwards the inbound caller's <c>Authorization</c> header onto the outbound
/// Warehouse proxy request. A capturing inner handler records the outbound request without any network.
/// </summary>
[TestFixture]
[Category("SDD-NOM-001")]
public sealed class BearerTokenForwardingHandlerTests
{
    private const string InboundToken = "Bearer inbound-user-token";

    private CapturingHandler _inner = null!;
    private Mock<IHttpContextAccessor> _httpContextAccessor = null!;

    /// <summary>Creates a fresh capturing inner handler and HTTP-context accessor before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _inner = new CapturingHandler();
        _httpContextAccessor = new Mock<IHttpContextAccessor>();
    }

    /// <summary>The inbound bearer token is copied onto the outbound Warehouse proxy request.</summary>
    [Test]
    public async Task WarehouseProxy_ForwardsInboundBearerToken_OnOutboundCall()
    {
        // Arrange
        DefaultHttpContext context = new();
        context.Request.Headers[HeaderNames.Authorization] = InboundToken;
        _httpContextAccessor.Setup(a => a.HttpContext).Returns(context);
        HttpClient client = BuildClient();

        // Act
        await client.GetAsync("https://warehouse.local/countries", CancellationToken.None);

        // Assert
        Assert.That(_inner.LastRequest, Is.Not.Null);
        AuthenticationHeaderValue? forwarded = _inner.LastRequest!.Headers.Authorization;
        Assert.Multiple(() =>
        {
            Assert.That(forwarded, Is.Not.Null);
            Assert.That(forwarded!.Scheme, Is.EqualTo("Bearer"));
            Assert.That(forwarded.Parameter, Is.EqualTo("inbound-user-token"));
        });
    }

    /// <summary>With no inbound token the outbound request carries no Authorization header.</summary>
    [Test]
    public async Task SendAsync_NoInboundToken_DoesNotSetAuthorization()
    {
        // Arrange
        _httpContextAccessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext());
        HttpClient client = BuildClient();

        // Act
        await client.GetAsync("https://warehouse.local/countries", CancellationToken.None);

        // Assert
        Assert.That(_inner.LastRequest!.Headers.Authorization, Is.Null);
    }

    private HttpClient BuildClient()
    {
        BearerTokenForwardingHandler handler = new(_httpContextAccessor.Object)
        {
            InnerHandler = _inner
        };
        return new HttpClient(handler);
    }

    /// <summary>Records the last outbound request and short-circuits with an empty 200 response.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        /// <summary>The most recent request observed by the handler.</summary>
        public HttpRequestMessage? LastRequest { get; private set; }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
