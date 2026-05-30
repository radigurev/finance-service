using Finance.EventLog.API.Extensions;
using Finance.EventLog.API.Interfaces;
using Finance.EventLog.API.Mapping;
using Finance.EventLog.API.Services;
using Finance.EventLog.DBModel;
using Finance.Infrastructure.Caching;
using Finance.Infrastructure.Web.Configuration;
using Finance.Infrastructure.Web.Extensions;
using Microsoft.EntityFrameworkCore;
using NLog;
using NLog.Web;
using Warehouse.Auth.AspNetCore;

Logger logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    logger.Info("Starting Finance.EventLog.API");

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    ConfigureServices(builder);

    WebApplication app = builder.Build();
    await ApplyMigrationsAsync(app).ConfigureAwait(false);
    app.UseFinanceServiceDefaults();
    app.MapControllers();
    app.Run();
}
catch (Exception ex)
{
    logger.Error(ex, "Application startup failed");
    throw;
}
finally
{
    LogManager.Shutdown();
}

static void ConfigureServices(WebApplicationBuilder builder)
{
    IServiceCollection services = builder.Services;
    IConfiguration configuration = builder.Configuration;

    string connectionString = configuration.GetConnectionString("FinanceEventLogDb")!;
    services.AddDbContext<EventLogDbContext>(options =>
        options.UseSqlServer(connectionString, sql =>
            sql.MigrationsAssembly(typeof(EventLogDbContext).Assembly.GetName().Name)));

    services.AddFinanceServiceDefaults(configuration, "finance-eventlog-api");

    ConfigurationValidation.ValidateFinanceJwtConfiguration(configuration);
    services.AddWarehouseAuthentication(configuration);
    services.AddWarehousePermissionValidation(configuration);

    services.AddFinanceRedisCache(configuration);
    services.AddEventLogConsumers(configuration);

    services.AddAutoMapper(typeof(EventLogMappingProfile).Assembly);

    services.AddHealthChecks()
        .AddDbContextCheck<EventLogDbContext>("eventlog-db", tags: ["ready"]);

    services.AddScoped<IEventQueryService, EventQueryService>();

    services.Configure<EventLogRetentionOptions>(
        configuration.GetSection(EventLogRetentionOptions.SectionName));
    services.AddScoped<IEventLogRetentionService, EventLogRetentionService>();
    services.AddHostedService<EventLogRetentionHostedService>();

    services.AddControllers();
}

static async Task ApplyMigrationsAsync(WebApplication app)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    EventLogDbContext db = scope.ServiceProvider.GetRequiredService<EventLogDbContext>();
    await db.Database.MigrateAsync().ConfigureAwait(false);
}

/// <summary>Sentinel type used by AutoMapper assembly scans.</summary>
public partial class Program;
