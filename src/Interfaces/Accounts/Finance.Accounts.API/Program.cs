using Asp.Versioning;
using FluentValidation;
using FluentValidation.AspNetCore;
using Finance.Accounts.API.Interfaces;
using Finance.Accounts.API.Mapping;
using Finance.Accounts.API.Services;
using Finance.Accounts.DBModel;
using Microsoft.EntityFrameworkCore;
using NLog;
using NLog.Web;
using Warehouse.Auth.AspNetCore;
using Warehouse.Correlation;

Logger logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    logger.Info("Starting Finance.Accounts.API");

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    ConfigureServices(builder);

    WebApplication app = builder.Build();
    ConfigurePipeline(app);
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

    services.AddCorrelationId();
    services.AddWarehouseAuthentication(configuration);

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

    services.AddFluentValidationAutoValidation();
    services.AddValidatorsFromAssemblyContaining<Program>();

    services.AddAutoMapper(typeof(AccountMappingProfile).Assembly);

    services.AddHealthChecks()
        .AddDbContextCheck<AccountsDbContext>("accounts-db", tags: ["ready"]);

    services.AddScoped<IAccountRepository, AccountRepository>();
    services.AddScoped<IAccountService, AccountService>();

    services.AddControllers();
    services.AddEndpointsApiExplorer();
    services.AddSwaggerGen();
}

static void ConfigurePipeline(WebApplication app)
{
    app.UseMiddleware<CorrelationIdMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapHealthChecks("/health/live");
    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });

    app.MapControllers();
}

/// <summary>Sentinel type used by AutoMapper / FluentValidation assembly scans.</summary>
public partial class Program;
