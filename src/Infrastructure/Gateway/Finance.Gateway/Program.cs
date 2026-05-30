using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using NLog;
using NLog.Web;
using Warehouse.Correlation;
using Finance.Gateway;

Logger logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    logger.Info("Starting Finance.Gateway");

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    builder.Services.AddReverseProxy()
        .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
        .AddTransforms(context =>
        {
            context.RequestTransforms.Add(new CorrelationIdRequestTransform());
        });

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.AddFixedWindowLimiter("fixed", limiter =>
        {
            limiter.PermitLimit = 100;
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            limiter.QueueLimit = 10;
        });

        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 200,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 20
                }));
    });

    GatewayConfigurationValidator.Validate(builder.Configuration);

    builder.Services.AddClusterReadinessHealthChecks(builder.Configuration);

    WebApplication app = builder.Build();

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseRateLimiter();

    app.MapHealthChecks("/health");
    app.MapReverseProxy();

    app.Run();
}
catch (Exception ex)
{
    logger.Error(ex, "Gateway startup failed");
    throw;
}
finally
{
    LogManager.Shutdown();
}

/// <summary>Entry-point sentinel exposed so <c>WebApplicationFactory&lt;Program&gt;</c> can host the gateway in-process for tests (SDD-INFRA-002 §2.6).</summary>
public partial class Program { }
