using FluentValidation;
using FluentValidation.AspNetCore;
using Finance.Common.Validation;
using Finance.Common.Workflow;
using Finance.Country.Abstractions;
using Finance.Country.BG;
using Finance.Infrastructure.Audit.Extensions;
using Finance.Infrastructure.Caching;
using Finance.Infrastructure.Messaging;
using Finance.Infrastructure.Sequences;
using Finance.Infrastructure.Services.Workflow;
using Finance.Infrastructure.Web.Configuration;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Infrastructure.Web.Extensions;
using Finance.Invoices.API.Consumers;
using Finance.Invoices.API.ErrorMapping;
using Finance.Invoices.API.Interfaces;
using Finance.Invoices.API.Mapping;
using Finance.Invoices.API.Services;
using Finance.Invoices.API.Validators;
using Finance.Invoices.API.Workflow;
using Finance.Invoices.DBModel;
using Finance.Invoices.DBModel.Models;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NLog;
using NLog.Web;
using Warehouse.Auth.AspNetCore;

Logger logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    logger.Info("Starting Finance.Invoices.API");

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

    string connectionString = configuration.GetConnectionString("FinanceInvoicesDb")!;
    services.AddDbContext<InvoicesDbContext>(options =>
        options.UseSqlServer(connectionString, sql =>
            sql.MigrationsAssembly(typeof(InvoicesDbContext).Assembly.GetName().Name)));

    services.AddFinanceServiceDefaults(configuration, "finance-invoices-api");

    ConfigurationValidation.ValidateFinanceJwtConfiguration(configuration);
    services.AddWarehouseAuthentication(configuration);
    services.AddWarehousePermissionValidation(configuration);

    services.AddFinanceRedisCache(configuration);
    services.AddFinanceMessageBus<InvoicesDbContext>(configuration, ConfigureConsumers);
    services.AddFinanceAudit<InvoicesDbContext>();
    services.AddSequenceGenerator<InvoicesDbContext>();

    services.AddFluentValidationAutoValidation();
    services.AddValidatorsFromAssemblyContaining<Program>();

    services.AddAutoMapper(typeof(InvoiceMappingProfile).Assembly);

    services.AddHealthChecks()
        .AddDbContextCheck<InvoicesDbContext>("invoices-db", tags: ["ready"]);

    // The Invoice domain extends the default error map for its 409 state-conflict codes (SDD-INV-001 §4).
    services.Replace(ServiceDescriptor.Singleton<IErrorCodeToStatusMap, InvoiceErrorCodeToStatusMap>());

    services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
    services.AddScoped<InvoiceTotalsCalculator>();
    services.AddScoped<IInvoiceService, InvoiceService>();

    // SDD-INT-WH-001 §2.1-§2.3: the shared map-and-create helper the four Warehouse inbound consumers use.
    services.AddScoped<IWarehouseInvoiceDraftFactory, WarehouseInvoiceDraftFactory>();

    // SDD-CTRY-001 §2.5: a single static binding for the country strategy — no factory/resolver.
    services.AddSingleton<ICountryStrategy, BulgariaStrategy>();

    ConfigureWorkflow(services);

    services.AddControllers();
}

static void ConfigureConsumers(IBusRegistrationConfigurator registration)
{
    // SDD-INV-001 §2.5: the Journal back-event consumer that links the posted JE and moves the invoice to
    // Posted. Wrapped transparently by UseFinanceIdempotency() (SDD-INFRA-006).
    registration.AddConsumer<InvoicePostedEventConsumer>();

    // SDD-INT-WH-001 §2.2, §2.5: the four Warehouse inbound consumers that materialize draft invoices.
    // Each is wrapped by the shared idempotency filter (UseFinanceIdempotency, SDD-INFRA-006) and inherits
    // the retry (1s/5s/15s) + <queue>_error dead-letter policy from AddFinanceMessageBus. The out-of-scope
    // ProductionOrderCompletedEvent / StockMovementRecordedEvent are deliberately NOT subscribed (§2.6).
    registration.AddConsumer<GoodsReceiptCompletedConsumer>();
    registration.AddConsumer<ShipmentCompletedConsumer>();
    registration.AddConsumer<CustomerReturnCompletedConsumer>();
    registration.AddConsumer<SupplierReturnShippedConsumer>();
}

static void ConfigureWorkflow(IServiceCollection services)
{
    services.AddScoped<IWorkflowState<Invoice>, DraftInvoiceState>();
    services.AddScoped<IWorkflowState<Invoice>, ConfirmedInvoiceState>();
    services.AddScoped<IWorkflowState<Invoice>, PostedInvoiceState>();
    services.AddScoped<IWorkflowState<Invoice>, CancelledInvoiceState>();
    services.AddScoped<IWorkflowState<Invoice>, ReversedInvoiceState>();

    services.AddScoped<IChainValidator<WorkflowContext<Invoice>>, InvoicePeriodWorkflowGuard>();

    // SDD-INV-001 §2.2: the default always-open guard; SDD-FIN-004 supplies the real period-status lookup.
    services.AddScoped<IInvoicePeriodGuard, AlwaysOpenInvoicePeriodGuard>();

    services.AddWorkflowEngine<Invoice>();
}

static async Task ApplyMigrationsAsync(WebApplication app)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    InvoicesDbContext db = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
    await db.Database.MigrateAsync().ConfigureAwait(false);
}

/// <summary>Sentinel type used by AutoMapper / FluentValidation assembly scans.</summary>
public partial class Program;
