using Warehouse.Correlation;
using Yarp.ReverseProxy.Transforms;

namespace Finance.Gateway;

/// <summary>
/// YARP request transform that injects the gateway-generated X-Correlation-ID
/// header into the proxied request so downstream services share the same trace ID.
/// </summary>
public sealed class CorrelationIdRequestTransform : RequestTransform
{
    /// <summary>Applies the correlation ID from HttpContext.Items to the outgoing proxy request header.</summary>
    public override ValueTask ApplyAsync(RequestTransformContext context)
    {
        string? correlationId = context.HttpContext.Items[CorrelationIdMiddleware.ItemKey] as string;
        if (!string.IsNullOrEmpty(correlationId))
        {
            context.ProxyRequest.Headers.Remove(CorrelationIdMiddleware.HeaderName);
            context.ProxyRequest.Headers.TryAddWithoutValidation(
                CorrelationIdMiddleware.HeaderName, correlationId);
        }
        return ValueTask.CompletedTask;
    }
}
