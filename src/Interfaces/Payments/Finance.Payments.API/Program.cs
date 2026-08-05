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
using Finance.Payments.API.Consumers;
using Finance.Payments.API.ErrorMapping;
using Finance.Payments.API.Http;
using Finance.Payments.API.Interfaces;
using Finance.Payments.API.Mapping;
using Finance.Payments.API.Services;
using Finance.Payments.API.Validation;
using Finance.Payments.API.Validators;
using Finance.Payments.API.Workflow;
using Finance.Payments.DBModel;
using Finance.Payments.DBModel.Models;
using MassTransit;
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
    logger.Info("Starting Finance.Payments.API");

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

    string connectionString = configuration.GetConnectionString("FinancePaymentsDb")!;
    services.AddDbContext<PaymentsDbContext>(options =>
        options.UseSqlServer(connectionString, sql =>
            sql.MigrationsAssembly(typeof(PaymentsDbContext).Assembly.GetName().Name)));

    services.AddFinanceServiceDefaults(configuration, "finance-payments-api");

    ConfigurationValidation.ValidateFinanceJwtConfiguration(configuration);
    services.AddWarehouseAuthentication(configuration);
    services.AddWarehousePermissionValidation(configuration);

    // SDD-PAY-001 §2.12: Redis is registered ONLY because the messaging idempotency filter needs the shared
    // IConnectionMultiplexer. Payments are transactional data and MUST NEVER be cached — no cache prefix is
    // registered and no ICacheService<T> is injected anywhere in this service.
    services.AddFinanceRedisCache(configuration);
    services.AddFinanceMessageBus<PaymentsDbContext>(configuration, ConfigureConsumers);
    services.AddFinanceAudit<PaymentsDbContext>();
    services.AddSequenceGenerator<PaymentsDbContext>();

    services.AddFluentValidationAutoValidation();
    services.AddValidatorsFromAssemblyContaining<Program>();

    services.AddAutoMapper(typeof(PaymentMappingProfile).Assembly);

    services.AddHealthChecks()
        .AddDbContextCheck<PaymentsDbContext>("payments-db", tags: ["ready"]);

    // The Payment domain extends the default error map for its 409 state/period/numbering codes
    // (SDD-PAY-001 §4) and the SDD-PAY-002 §4 allocation conflict codes — sixteen explicit entries in ONE
    // shared map, registered exactly once.
    services.Replace(ServiceDescriptor.Singleton<IErrorCodeToStatusMap, PaymentErrorCodeToStatusMap>());

    services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
    services.AddScoped<PaymentAmountCalculator>();
    services.AddScoped<IPaymentService, PaymentService>();

    ConfigureAllocation(services);
    ConfigureAging(services);

    // SDD-CTRY-001 §2.5: a single static binding for the country strategy — no factory/resolver.
    services.AddSingleton<ICountryStrategy, BulgariaStrategy>();

    ConfigureWorkflow(services);
    ConfigureReferenceDataClients(services, configuration);

    services.AddControllers();
}

static void ConfigureConsumers(IBusRegistrationConfigurator registration)
{
    // SDD-PAY-001 §2.5: the Journal back-event consumer that links the posted JE and moves the payment to
    // Posted. Wrapped transparently by UseFinanceIdempotency() (SDD-INFRA-006) and covered by the shared
    // retry (1s/5s/15s) + <queue>_error dead-letter policy from AddFinanceMessageBus.
    registration.AddConsumer<PaymentPostedEventConsumer>();

    // SDD-PAY-002 §2.3: the FOUR consumers that feed the local InvoiceOpenItem read projection from the
    // invoice's own domain events, so allocation and aging never cross-join finance_invoices and never depend
    // on the Invoices service being reachable. Each is idempotent through the same shared filter.
    registration.AddConsumer<InvoiceConfirmedEventConsumer>();
    registration.AddConsumer<InvoicePostedEventConsumer>();
    registration.AddConsumer<InvoiceCancelledEventConsumer>();
    registration.AddConsumer<InvoiceReversedEventConsumer>();
}

static void ConfigureAllocation(IServiceCollection services)
{
    services.AddScoped<IInvoiceOpenItemProjection, InvoiceOpenItemProjection>();
    services.AddScoped<AllocationAmountCalculator>();
    services.AddScoped<SettlementStatusCalculator>();
    services.AddScoped<IPaymentAllocationService, PaymentAllocationService>();

    // SDD-PAY-002 §2.9: the realized-FX seam is DORMANT — the only registered handler is the no-op, so
    // allocation works end-to-end while SDD-FIN-005 is unauthored. Posting the difference is that spec's.
    services.AddScoped<IRealizedFxHandler, NoOpRealizedFxHandler>();

    // SDD-PAY-002 §2.5: the TEN cross-aggregate invariant rules, registered in the documented order. The
    // chain short-circuits on the first failure, so the control-account rule sits LAST and the cheaper,
    // more specific direction diagnostic fires first for a request that breaks both.
    services.AddValidationChain<PaymentAllocationValidationContext>(
        typeof(PaymentAllocatableValidator),
        typeof(AllocationInvoiceKnownValidator),
        typeof(AllocationInvoiceEligibleValidator),
        typeof(AllocationDirectionValidator),
        typeof(AllocationCounterpartyValidator),
        typeof(AllocationCurrencyValidator),
        typeof(AllocationDuplicateValidator),
        typeof(AllocationWithinPaymentValidator),
        typeof(AllocationWithinOutstandingValidator),
        typeof(AllocationControlAccountValidator));
}

static void ConfigureAging(IServiceCollection services)
{
    // SDD-PAY-003: a READ-ONLY aggregation over the SDD-PAY-002 open-item projection. It adds no table, no
    // migration, no event, no consumer, no audit row, and no workflow state — so nothing else is registered here.
    // The bucket calculator is a bare concrete class (in the manner of SettlementStatusCalculator) because it is
    // pure and must stay unit-testable without a database.
    services.AddScoped<AgingBucketCalculator>();
    services.AddScoped<IAgingService, AgingService>();
}

static void ConfigureWorkflow(IServiceCollection services)
{
    services.AddScoped<IWorkflowState<Payment>, DraftPaymentState>();
    services.AddScoped<IWorkflowState<Payment>, ConfirmedPaymentState>();
    services.AddScoped<IWorkflowState<Payment>, PostedPaymentState>();
    services.AddScoped<IWorkflowState<Payment>, CancelledPaymentState>();
    services.AddScoped<IWorkflowState<Payment>, ReversedPaymentState>();

    services.AddScoped<IChainValidator<WorkflowContext<Payment>>, PaymentPeriodWorkflowGuard>();

    // SDD-PAY-001 §2.9: gateway-backed from day one and failing closed. AlwaysOpenPaymentPeriodGuard exists
    // for unit tests only and MUST NOT be registered here.
    services.AddScoped<IPaymentPeriodGuard, GatewayPaymentPeriodGuard>();

    services.AddWorkflowEngine<Payment>();
}

static void ConfigureReferenceDataClients(IServiceCollection services, IConfiguration configuration)
{
    string gatewayBaseUrl = configuration["Gateway:BaseUrl"]
        ?? throw new InvalidOperationException(
            "Gateway:BaseUrl is required for the settlement-account and fiscal-period reads (SDD-PAY-001 §2.8, §2.9).");

    services.AddTransient<CorrelationIdDelegatingHandler>();
    services.AddTransient<BearerTokenForwardingHandler>();

    services.AddRefitClient<IAccountReadClient>()
        .ConfigureHttpClient(client => client.BaseAddress = new Uri(gatewayBaseUrl))
        .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
        .AddHttpMessageHandler<BearerTokenForwardingHandler>()
        .AddStandardResilienceHandler();

    services.AddRefitClient<IPeriodReadClient>()
        .ConfigureHttpClient(client => client.BaseAddress = new Uri(gatewayBaseUrl))
        .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
        .AddHttpMessageHandler<BearerTokenForwardingHandler>()
        .AddStandardResilienceHandler();

    services.AddScoped<ISettlementAccountReader, GatewaySettlementAccountReader>();
}

static async Task ApplyMigrationsAsync(WebApplication app)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    PaymentsDbContext db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
    await db.Database.MigrateAsync().ConfigureAwait(false);
}

/// <summary>Sentinel type used by AutoMapper / FluentValidation assembly scans.</summary>
public partial class Program;
