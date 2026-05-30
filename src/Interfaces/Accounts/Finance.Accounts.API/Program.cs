using FluentValidation;
using FluentValidation.AspNetCore;
using Finance.Accounts.API.Interfaces;
using Finance.Accounts.API.Mapping;
using Finance.Accounts.API.Services;
using Finance.Accounts.API.Validators;
using Finance.Accounts.DBModel;
using Finance.Common.Validation;
using Finance.Infrastructure.Audit.Extensions;
using Finance.Infrastructure.Caching;
using Finance.Infrastructure.Messaging;
using Finance.Infrastructure.Web.Configuration;
using Finance.Infrastructure.Web.Extensions;
using Finance.ServiceModel.Accounts;
using Microsoft.EntityFrameworkCore;
using NLog;
using NLog.Web;
using Warehouse.Auth.AspNetCore;

Logger logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    logger.Info("Starting Finance.Accounts.API");

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

    string connectionString = configuration.GetConnectionString("FinanceAccountsDb")!;
    services.AddDbContext<AccountsDbContext>(options =>
        options.UseSqlServer(connectionString, sql =>
            sql.MigrationsAssembly(typeof(AccountsDbContext).Assembly.GetName().Name)));

    services.AddFinanceServiceDefaults(configuration, "finance-accounts-api");

    ConfigurationValidation.ValidateFinanceJwtConfiguration(configuration);
    services.AddWarehouseAuthentication(configuration);
    services.AddWarehousePermissionValidation(configuration);

    services.AddFinanceRedisCache(configuration);
    services.AddFinanceMessageBus<AccountsDbContext>(configuration);
    services.AddFinanceAudit<AccountsDbContext>();

    services.AddFluentValidationAutoValidation();
    services.AddValidatorsFromAssemblyContaining<Program>();

    services.AddAutoMapper(typeof(AccountMappingProfile).Assembly);

    services.AddHealthChecks()
        .AddDbContextCheck<AccountsDbContext>("accounts-db", tags: ["ready"]);

    services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
    services.AddScoped<IAccountRepository, AccountRepository>();
    services.AddScoped<IAccountService, AccountService>();

    services.AddValidationChain<CreateAccountRequest>(
        typeof(DuplicateAccountCodeValidator),
        typeof(ParentAccountValidator));

    services.AddControllers();
}

static async Task ApplyMigrationsAsync(WebApplication app)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    AccountsDbContext db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
    await db.Database.MigrateAsync().ConfigureAwait(false);
}

/// <summary>Sentinel type used by AutoMapper / FluentValidation assembly scans.</summary>
public partial class Program;
