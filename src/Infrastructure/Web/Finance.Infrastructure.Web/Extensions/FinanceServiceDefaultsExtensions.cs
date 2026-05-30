using Asp.Versioning;
using Finance.Infrastructure.Web.Correlation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Warehouse.Correlation;
using ICorrelationIdAccessor = Finance.Common.Abstractions.ICorrelationIdAccessor;

namespace Finance.Infrastructure.Web.Extensions;

/// <summary>
/// The shared host-builder + pipeline bundle that every Finance microservice composes to remove the
/// per-service <c>Program.cs</c> drift (SDD-INFRA-001 §1): correlation, ProblemDetails, observability,
/// API versioning, health-check infrastructure, and dev-only Swagger. Services still register their own
/// <c>DbContext</c>, <c>AddWarehouseAuthentication</c>, and <c>AddDbContextCheck&lt;TContext&gt;</c>.
/// </summary>
public static class FinanceServiceDefaultsExtensions
{
    /// <summary>
    /// Registers the Finance baseline services: correlation id (interface + HTTP accessor), ProblemDetails
    /// customization, OpenTelemetry tracing, URL-segment API versioning (v1 default), health checks, and
    /// the Swagger generator.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="serviceName">The kebab-case service name used for the OTLP <c>service.name</c>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddFinanceServiceDefaults(
        this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddCorrelationId();
        services.AddHttpContextAccessor();
        services.TryAddSingleton<ICorrelationIdAccessor, HttpContextCorrelationIdAccessor>();

        services.AddFinanceProblemDetails();
        services.AddFinanceObservability(configuration, serviceName);

        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
        }).AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        });

        services.AddHealthChecks();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        return services;
    }

    /// <summary>
    /// Configures the Finance baseline pipeline: correlation middleware, the global exception handler,
    /// dev-only Swagger, authentication/authorization ordering, and the liveness/readiness health endpoints.
    /// </summary>
    /// <param name="app">The application pipeline to configure.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    public static WebApplication UseFinanceServiceDefaults(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHealthChecks("/health/live");
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        });

        return app;
    }
}
