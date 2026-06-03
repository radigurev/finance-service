using System.Net.Http.Headers;
using Microsoft.Net.Http.Headers;

namespace Finance.Journal.API.Http;

/// <summary>
/// Outbound <see cref="DelegatingHandler"/> that forwards the inbound caller's bearer token onto the
/// Accounts / Nomenclature read calls made through the Finance Gateway (SDD-FIN-001 §2.6, §2.7).
/// Service-to-service JWT is deferred until <c>SDD-INT-WH-002</c> is drafted; until then the originating
/// user's <c>Authorization</c> header is copied onto the outbound call so the downstream service
/// authorizes the request as that user.
/// </summary>
public sealed class BearerTokenForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Creates a new <see cref="BearerTokenForwardingHandler"/>.</summary>
    /// <param name="httpContextAccessor">The accessor for the ambient HTTP context carrying the inbound token.</param>
    public BearerTokenForwardingHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ForwardInboundAuthorization(request);
        return base.SendAsync(request, cancellationToken);
    }

    private void ForwardInboundAuthorization(HttpRequestMessage request)
    {
        if (request.Headers.Authorization is not null)
        {
            return;
        }

        string? inboundAuthorization = _httpContextAccessor.HttpContext?.Request
            .Headers[HeaderNames.Authorization];

        if (!string.IsNullOrWhiteSpace(inboundAuthorization)
            && AuthenticationHeaderValue.TryParse(inboundAuthorization, out AuthenticationHeaderValue? parsed))
        {
            request.Headers.Authorization = parsed;
        }
    }
}
