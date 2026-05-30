using Finance.Common.Abstractions;
using Finance.Infrastructure.Web.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Finance.Infrastructure.Web.Extensions;

/// <summary>
/// DI registration of the Finance OpenTelemetry tracing baseline (SDD-OBS-001 §2.3, §2.4): ASP.NET Core,
/// HttpClient, and EF Core instrumentation, the OTLP exporter, the <c>service.name</c> resource, and the
/// <see cref="CorrelationIdSpanProcessor"/> that stamps the ambient correlation id onto each span.
/// Metrics (<c>/metrics</c>) and Grafana dashboards are deferred to Phase 7.
/// </summary>
public static class ObservabilityServiceCollectionExtensions
{
    /// <summary>The default OTLP endpoint used when <c>OpenTelemetry:OtlpEndpoint</c> is unset.</summary>
    public const string DefaultOtlpEndpoint = "http://platform-jaeger:4317";

    /// <summary>
    /// Registers OpenTelemetry tracing with the OTLP exporter pointed at <c>OpenTelemetry:OtlpEndpoint</c>
    /// and tags spans with the ambient correlation id.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configuration">The application configuration supplying the OTLP endpoint.</param>
    /// <param name="serviceName">The kebab-case service name set as <c>service.name</c>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddFinanceObservability(
        this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"] ?? DefaultOtlpEndpoint;

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddProcessor(BuildCorrelationProcessor)
                .AddOtlpExporter(exporter => exporter.Endpoint = new Uri(otlpEndpoint)));

        return services;
    }

    private static CorrelationIdSpanProcessor BuildCorrelationProcessor(IServiceProvider provider)
    {
        ICorrelationIdAccessor accessor = provider.GetRequiredService<ICorrelationIdAccessor>();
        return new CorrelationIdSpanProcessor(accessor);
    }
}
