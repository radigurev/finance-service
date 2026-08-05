using System.Net.Http.Headers;
using Microsoft.Net.Http.Headers;

namespace Finance.Payments.API.Http;

/// <summary>
/// Outbound <see cref="DelegatingHandler"/> that forwards the inbound caller's bearer token onto the Accounts
/// and Periods read calls made through the Finance Gateway (SDD-PAY-001 §2.8, §2.9). Service-to-service JWT is
/// deferred to SDD-INT-WH-002; until then the originating user's <c>Authorization</c> header is copied onto the
/// outbound call so the downstream service authorizes the request as that user.
/// <para>This is a per-service copy: the handler exists as independent copies in
/// <c>Finance.Journal.API</c> and <c>Finance.Nomenclature.API</c> and is not shared infrastructure today.
/// Promoting it into <c>Finance.Infrastructure.Web</c> is a recorded SDD-INFRA-009 change
/// (SDD-PAY-001 §2.8, §7).</para>
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
