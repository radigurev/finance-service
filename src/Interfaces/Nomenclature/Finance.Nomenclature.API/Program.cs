using FluentValidation;
using FluentValidation.AspNetCore;
using Finance.Common.Validation;
using Finance.Infrastructure.Audit.Extensions;
using Finance.Infrastructure.Caching;
using Finance.Infrastructure.Messaging;
using Finance.Infrastructure.Web.Configuration;
using Finance.Infrastructure.Web.Extensions;
using Finance.Nomenclature.API.Http;
using Finance.Nomenclature.API.Interfaces;
using Finance.Nomenclature.API.Mapping;
using Finance.Nomenclature.API.Services;
using Finance.Nomenclature.API.Validators;
using Finance.Nomenclature.DBModel;
using Finance.ServiceModel.Nomenclature;
using Microsoft.EntityFrameworkCore;
using Microsoft.FeatureManagement;
using NLog;
using NLog.Web;
using Refit;
using Warehouse.Auth.AspNetCore;
using Warehouse.Correlation;

Logger logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    logger.Info("Starting Finance.Nomenclature.API");

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    ConfigureServices(builder);

    WebApplication app = builder.Build();
    await ApplyMigrationsAsync(app).ConfigureAwait(false);
    await SeedCurrenciesAsync(app).ConfigureAwait(false);
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

    string connectionString = configuration.GetConnectionString("FinanceNomenclatureDb")!;
    services.AddDbContext<NomenclatureDbContext>(options =>
        options.UseSqlServer(connectionString, sql =>
            sql.MigrationsAssembly(typeof(NomenclatureDbContext).Assembly.GetName().Name)));

    services.AddFinanceServiceDefaults(configuration, "finance-nomenclature-api");

    ConfigurationValidation.ValidateFinanceJwtConfiguration(configuration);
    services.AddWarehouseAuthentication(configuration);
    services.AddWarehousePermissionValidation(configuration);

    services.AddFinanceRedisCache(configuration);
    services.AddFinanceMessageBus<NomenclatureDbContext>(configuration);
    services.AddFinanceAudit<NomenclatureDbContext>();

    services.AddFeatureManagement();

    services.AddFluentValidationAutoValidation();
    services.AddValidatorsFromAssemblyContaining<Program>();

    services.AddAutoMapper(typeof(NomenclatureMappingProfile).Assembly);

    services.AddHealthChecks()
        .AddDbContextCheck<NomenclatureDbContext>("nomenclature-db", tags: ["ready"]);

    services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
    services.AddScoped<ICurrencyService, CurrencyService>();
    services.AddScoped<IExchangeRateService, ExchangeRateService>();
    services.AddScoped<IWarehouseProxyService, WarehouseProxyService>();
    services.AddScoped<ICurrencySeeder, Iso4217CurrencySeeder>();

    services.AddValidationChain<CreateCurrencyRequest>(
        typeof(DuplicateCurrencyCodeValidator));

    ConfigureWarehouseProxyClient(services, configuration);

    services.AddControllers();
}

static void ConfigureWarehouseProxyClient(IServiceCollection services, IConfiguration configuration)
{
    string warehouseBaseUrl = configuration["Warehouse:NomenclatureBaseUrl"]
        ?? throw new InvalidOperationException(
            "Warehouse:NomenclatureBaseUrl is required for the country/state/city proxy (SDD-NOM-001 §2.3).");

    services.AddTransient<CorrelationIdDelegatingHandler>();
    services.AddTransient<BearerTokenForwardingHandler>();

    services.AddRefitClient<IWarehouseNomenclatureClient>()
        .ConfigureHttpClient(client => client.BaseAddress = new Uri(warehouseBaseUrl))
        .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
        .AddHttpMessageHandler<BearerTokenForwardingHandler>()
        .AddStandardResilienceHandler();
}

static async Task ApplyMigrationsAsync(WebApplication app)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    NomenclatureDbContext db = scope.ServiceProvider.GetRequiredService<NomenclatureDbContext>();
    await db.Database.MigrateAsync().ConfigureAwait(false);
}

static async Task SeedCurrenciesAsync(WebApplication app)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    IFeatureManager features = scope.ServiceProvider.GetRequiredService<IFeatureManager>();

    if (!await features.IsEnabledAsync("EnableCurrencySeeding").ConfigureAwait(false))
    {
        return;
    }

    ICurrencySeeder seeder = scope.ServiceProvider.GetRequiredService<ICurrencySeeder>();
    await seeder.SeedAsync(CancellationToken.None).ConfigureAwait(false);
}

/// <summary>Sentinel type used by AutoMapper / FluentValidation assembly scans.</summary>
public partial class Program;
