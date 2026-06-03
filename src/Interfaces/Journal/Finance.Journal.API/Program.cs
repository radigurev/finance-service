using FluentValidation;
using FluentValidation.AspNetCore;
using Finance.Common.Validation;
using Finance.Common.Workflow;
using Finance.Infrastructure.Audit.Extensions;
using Finance.Infrastructure.Caching;
using Finance.Infrastructure.Messaging;
using Finance.Infrastructure.Sequences;
using Finance.Infrastructure.Services.Workflow;
using Finance.Infrastructure.Web.Configuration;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Infrastructure.Web.Extensions;
using Finance.Journal.API.ErrorMapping;
using Finance.Journal.API.Http;
using Finance.Journal.API.Interfaces;
using Finance.Journal.API.Mapping;
using Finance.Journal.API.Services;
using Finance.Journal.API.Validation;
using Finance.Journal.API.Workflow;
using Finance.Journal.DBModel;
using Finance.Journal.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NLog;
using NLog.Web;
using Refit;
using Warehouse.Auth.AspNetCore;
using Warehouse.Correlation;

Logger logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    logger.Info("Starting Finance.Journal.API");

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

    string connectionString = configuration.GetConnectionString("FinanceJournalDb")!;
    services.AddDbContext<JournalDbContext>(options =>
        options.UseSqlServer(connectionString, sql =>
            sql.MigrationsAssembly(typeof(JournalDbContext).Assembly.GetName().Name)));

    services.AddFinanceServiceDefaults(configuration, "finance-journal-api");

    ConfigurationValidation.ValidateFinanceJwtConfiguration(configuration);
    services.AddWarehouseAuthentication(configuration);
    services.AddWarehousePermissionValidation(configuration);

    services.AddFinanceRedisCache(configuration);
    services.AddFinanceMessageBus<JournalDbContext>(configuration);
    services.AddFinanceAudit<JournalDbContext>();
    services.AddSequenceGenerator<JournalDbContext>();

    services.AddFluentValidationAutoValidation();
    services.AddValidatorsFromAssemblyContaining<Program>();

    services.AddAutoMapper(typeof(JournalMappingProfile).Assembly);

    services.AddHealthChecks()
        .AddDbContextCheck<JournalDbContext>("journal-db", tags: ["ready"]);

    // The Journal domain extends the default error map for its 409 state-conflict codes (SDD-FIN-002 §4).
    services.Replace(ServiceDescriptor.Singleton<IErrorCodeToStatusMap, JournalErrorCodeToStatusMap>());

    services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
    services.AddScoped<IJournalEntryValidator, JournalEntryValidator>();
    services.AddScoped<IJournalEntryService, JournalEntryService>();
    services.AddScoped<IGeneralLedgerService, GeneralLedgerService>();

    ConfigureWorkflow(services);
    ConfigureValidationChain(services);
    ConfigureReferenceDataClients(services, configuration);

    services.AddControllers();
}

static void ConfigureWorkflow(IServiceCollection services)
{
    services.AddScoped<IWorkflowState<JournalEntry>, DraftJournalEntryState>();
    services.AddScoped<IWorkflowState<JournalEntry>, PostedJournalEntryState>();
    services.AddScoped<IWorkflowState<JournalEntry>, ReversedJournalEntryState>();

    services.AddScoped<IChainValidator<WorkflowContext<JournalEntry>>, PostingPeriodWorkflowGuard>();

    // SDD-FIN-004 §2.7: the dormant AlwaysOpenPostingPeriodGuard is replaced in production by the
    // gateway-backed guard that activates POSTING_PERIOD_CLOSED. AlwaysOpenPostingPeriodGuard remains in
    // the codebase as the default unit-test fallback.
    services.AddScoped<IPostingPeriodGuard, GatewayPostingPeriodGuard>();

    services.AddWorkflowEngine<JournalEntry>();
}

static void ConfigureValidationChain(IServiceCollection services)
{
    services.AddValidationChain<JournalEntryValidationContext>(
        typeof(BalanceValidator),
        typeof(LineBaseAmountValidator),
        typeof(AccountPostabilityValidator),
        typeof(LineCurrencyValidator));
}

static void ConfigureReferenceDataClients(IServiceCollection services, IConfiguration configuration)
{
    string gatewayBaseUrl = configuration["Gateway:BaseUrl"]
        ?? throw new InvalidOperationException(
            "Gateway:BaseUrl is required for the Accounts/Currencies postability reads (SDD-FIN-001 §2.6).");

    services.AddTransient<CorrelationIdDelegatingHandler>();
    services.AddTransient<BearerTokenForwardingHandler>();

    services.AddRefitClient<IAccountReadClient>()
        .ConfigureHttpClient(client => client.BaseAddress = new Uri(gatewayBaseUrl))
        .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
        .AddHttpMessageHandler<BearerTokenForwardingHandler>()
        .AddStandardResilienceHandler();

    services.AddRefitClient<ICurrencyReadClient>()
        .ConfigureHttpClient(client => client.BaseAddress = new Uri(gatewayBaseUrl))
        .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
        .AddHttpMessageHandler<BearerTokenForwardingHandler>()
        .AddStandardResilienceHandler();

    // SDD-FIN-004 §2.7: the posting-period guard reads period status through the gateway with the same
    // handler chain as the Accounts / Currencies reference clients.
    services.AddRefitClient<IPeriodReadClient>()
        .ConfigureHttpClient(client => client.BaseAddress = new Uri(gatewayBaseUrl))
        .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
        .AddHttpMessageHandler<BearerTokenForwardingHandler>()
        .AddStandardResilienceHandler();

    services.AddScoped<IReferenceDataReader, GatewayReferenceDataReader>();
}

static async Task ApplyMigrationsAsync(WebApplication app)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    JournalDbContext db = scope.ServiceProvider.GetRequiredService<JournalDbContext>();
    await db.Database.MigrateAsync().ConfigureAwait(false);
}

/// <summary>Sentinel type used by AutoMapper / FluentValidation assembly scans.</summary>
public partial class Program;
