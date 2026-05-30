using Microsoft.AspNetCore.Http;
using Warehouse.Correlation;
using ICorrelationIdAccessor = Finance.Common.Abstractions.ICorrelationIdAccessor;

namespace Finance.Infrastructure.Web.Correlation;

/// <summary>
/// <see cref="ICorrelationIdAccessor"/> implementation that reads the ambient correlation id stamped
/// by the <c>Warehouse.Correlation</c> <see cref="CorrelationIdMiddleware"/> onto
/// <see cref="HttpContext.Items"/> (the same <see cref="CorrelationIdMiddleware.ItemKey"/> the gateway's
/// request transform reads). Falls back to the request header, then to a freshly generated GUID when no
/// ambient context is present (SDD-INFRA-001 §1, SDD-OBS-001 §2.4).
/// </summary>
public sealed class HttpContextCorrelationIdAccessor : ICorrelationIdAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Initializes the accessor with the ambient <see cref="IHttpContextAccessor"/>.</summary>
    /// <param name="httpContextAccessor">The accessor for the current request's <see cref="HttpContext"/>.</param>
    public HttpContextCorrelationIdAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public string Get()
    {
        HttpContext? httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return Guid.NewGuid().ToString();
        }

        if (httpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out object? value)
            && value is string fromItems
            && !string.IsNullOrEmpty(fromItems))
        {
            return fromItems;
        }

        string? fromHeader = httpContext.Request.Headers[CorrelationIdMiddleware.HeaderName];
        if (!string.IsNullOrEmpty(fromHeader))
        {
            return fromHeader;
        }

        return Guid.NewGuid().ToString();
    }
}
