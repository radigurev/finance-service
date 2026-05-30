using Finance.EventLog.API.Consumers;
using Finance.EventLog.API.Interfaces;
using Finance.EventLog.API.Mapping;
using Finance.EventLog.DBModel;
using Finance.Infrastructure.Messaging;
using Finance.Infrastructure.Messaging.Filters;
using Finance.ServiceModel.Events.Accounts;
using Finance.ServiceModel.Events.Nomenclature;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Finance.EventLog.API.Tests.Fixtures;

/// <summary>
/// Wires the MassTransit in-memory test harness (via the shared
/// <see cref="FinanceMessagingTestHarnessExtensions.AddFinanceMessagingTestHarness"/>) with the six EventLog
/// consumers, their per-type mapping strategies, the shared SQLite-backed <see cref="EventLogDbContext"/>, a
/// SETNX-emulating Redis seam, and the production <see cref="IdempotencyFilter{T}"/> on the in-memory
/// transport (SDD-EVTLOG-001 §6, SDD-INFRA-006 §2.5). No RabbitMQ, no Redis, no SQL Server is required — the
/// consume path is exercised exactly as in production, including duplicate-message-id suppression.
/// </summary>
public sealed class EventLogConsumerTestHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private EventLogConsumerTestHarness(ServiceProvider provider, EventLogDbContext db)
    {
        _provider = provider;
        Db = db;
    }

    /// <summary>The SQLite-backed event-log context the consumers write to.</summary>
    public EventLogDbContext Db { get; }

    /// <summary>The started MassTransit in-memory test harness.</summary>
    public ITestHarness Harness => _provider.GetRequiredService<ITestHarness>();

    /// <summary>
    /// Builds and starts a harness bound to the supplied SQLite context.
    /// </summary>
    /// <param name="db">The SQLite-backed event-log context shared with the consumers.</param>
    /// <returns>A started, disposable harness.</returns>
    public static async Task<EventLogConsumerTestHarness> StartAsync(EventLogDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(db);
        services.AddSingleton<IConnectionMultiplexer>(FakeSetNxRedis.Create());
        RegisterStrategies(services);

        services.AddFinanceMessagingTestHarness(RegisterConsumers);

        ServiceProvider provider = services.BuildServiceProvider(true);
        ITestHarness harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        return new EventLogConsumerTestHarness(provider, db);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
    }

    private static void RegisterStrategies(IServiceCollection services)
    {
        services.AddScoped<IEventMappingStrategy<AccountCreatedEvent>, AccountCreatedEventMappingStrategy>();
        services.AddScoped<IEventMappingStrategy<AccountUpdatedEvent>, AccountUpdatedEventMappingStrategy>();
        services.AddScoped<IEventMappingStrategy<AccountDeactivatedEvent>, AccountDeactivatedEventMappingStrategy>();
        services.AddScoped<IEventMappingStrategy<CurrencyCreatedEvent>, CurrencyCreatedEventMappingStrategy>();
        services.AddScoped<IEventMappingStrategy<CurrencyUpdatedEvent>, CurrencyUpdatedEventMappingStrategy>();
        services.AddScoped<IEventMappingStrategy<CurrencyDeactivatedEvent>, CurrencyDeactivatedEventMappingStrategy>();
    }

    private static void RegisterConsumers(IBusRegistrationConfigurator registration)
    {
        registration.AddConsumer<AccountCreatedEventConsumer>();
        registration.AddConsumer<AccountUpdatedEventConsumer>();
        registration.AddConsumer<AccountDeactivatedEventConsumer>();
        registration.AddConsumer<CurrencyCreatedEventConsumer>();
        registration.AddConsumer<CurrencyUpdatedEventConsumer>();
        registration.AddConsumer<CurrencyDeactivatedEventConsumer>();

        registration.UsingInMemory((context, bus) =>
        {
            bus.UseConsumeFilter(typeof(IdempotencyFilter<>), context);
            bus.ConfigureEndpoints(context);
        });
    }
}
