using FluentValidation;
using FluentValidation.AspNetCore;
using Finance.Common.Validation;
using Finance.Common.Workflow;
using Finance.Infrastructure.Audit.Extensions;
using Finance.Infrastructure.Caching;
using Finance.Infrastructure.Messaging;
using Finance.Infrastructure.Services.Workflow;
using Finance.Infrastructure.Web.Configuration;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Infrastructure.Web.Extensions;
using Finance.Periods.API.ErrorMapping;
using Finance.Periods.API.Interfaces;
using Finance.Periods.API.Mapping;
using Finance.Periods.API.Services;
using Finance.Periods.API.Workflow;
using Finance.Periods.DBModel;
using Finance.Periods.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NLog;
using NLog.Web;
using Warehouse.Auth.AspNetCore;

Logger logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    logger.Info("Starting Finance.Periods.API");

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

    string connectionString = configuration.GetConnectionString("FinancePeriodsDb")!;
    services.AddDbContext<PeriodsDbContext>(options =>
        options.UseSqlServer(connectionString, sql =>
            sql.MigrationsAssembly(typeof(PeriodsDbContext).Assembly.GetName().Name)));

    services.AddFinanceServiceDefaults(configuration, "finance-periods-api");

    ConfigurationValidation.ValidateFinanceJwtConfiguration(configuration);
    services.AddWarehouseAuthentication(configuration);
    services.AddWarehousePermissionValidation(configuration);

    services.AddFinanceRedisCache(configuration);
    services.AddFinanceMessageBus<PeriodsDbContext>(configuration);
    services.AddFinanceAudit<PeriodsDbContext>();

    services.AddFluentValidationAutoValidation();
    services.AddValidatorsFromAssemblyContaining<Program>();

    services.AddAutoMapper(typeof(PeriodMappingProfile).Assembly);

    services.AddHealthChecks()
        .AddDbContextCheck<PeriodsDbContext>("periods-db", tags: ["ready"]);

    // The Periods domain extends the default error map for its 409 state / ordering / uniqueness codes
    // and the 404 NO_PERIOD_FOR_DATE code (SDD-FIN-004 §5).
    services.Replace(ServiceDescriptor.Singleton<IErrorCodeToStatusMap, PeriodErrorCodeToStatusMap>());

    services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
    services.AddSingleton<IFiscalCalendar, CalendarMonthFiscalCalendar>();
    services.AddScoped<IFiscalPeriodService, FiscalPeriodService>();

    ConfigureWorkflow(services);

    services.AddControllers();
}

static void ConfigureWorkflow(IServiceCollection services)
{
    services.AddScoped<IWorkflowState<FiscalPeriod>, OpenFiscalPeriodState>();
    services.AddScoped<IWorkflowState<FiscalPeriod>, ClosedFiscalPeriodState>();

    services.AddScoped<IChainValidator<WorkflowContext<FiscalPeriod>>, PeriodOrderingWorkflowGuard>();

    services.AddWorkflowEngine<FiscalPeriod>();
}

static async Task ApplyMigrationsAsync(WebApplication app)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    PeriodsDbContext db = scope.ServiceProvider.GetRequiredService<PeriodsDbContext>();
    await db.Database.MigrateAsync().ConfigureAwait(false);
}

/// <summary>Sentinel type used by AutoMapper / FluentValidation assembly scans.</summary>
public partial class Program;
