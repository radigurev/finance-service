using Finance.Common.ErrorCodes;
using Finance.EventLog.API.Consumers;
using Finance.EventLog.API.Interfaces;
using Finance.EventLog.API.Mapping;
using Finance.Infrastructure.Messaging;
using Finance.Infrastructure.Messaging.Configuration;
using Finance.ServiceModel.Events.Accounts;
using Finance.ServiceModel.Events.Nomenclature;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.EventLog.API.Extensions;

/// <summary>
/// Consume-only MassTransit registration for the EventLog service (SDD-EVTLOG-001 §2.1-§2.3). Unlike the
/// publishing services, EventLog does not emit events, so the EF transactional outbox is intentionally
/// skipped (SDD-INFRA-006 §2.1 is optional here). This extension registers the six Finance-event consumers,
/// their per-type <see cref="IEventMappingStrategy{TEvent}"/> strategies, the RabbitMQ transport with the
/// shared retry/dead-letter policy, and the Redis-backed <c>UseFinanceIdempotency()</c> filter so replays
/// never produce a duplicate archive row.
/// </summary>
public static class EventLogMessagingExtensions
{
    private static readonly TimeSpan[] RetryIntervals =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15)
    ];

    /// <summary>
    /// Registers the consume-only Finance message bus for EventLog: the six event consumers + strategies,
    /// the RabbitMQ host (validated against <c>RabbitMQ:Host</c>), retry/DLQ, idempotency, and endpoint
    /// conventions. Startup fails fast when <c>RabbitMQ:Host</c> is missing (SDD-INFRA-006 §3).
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">Configuration carrying the <c>RabbitMQ</c> section.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddEventLogConsumers(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        RabbitMqOptions options = RequireRabbitMqOptions(configuration);
        RegisterMappingStrategies(services);

        services.AddMassTransit(registration =>
        {
            RegisterConsumers(registration);
            ConfigureRabbitMqTransport(registration, options);
        });

        return services;
    }

    private static void RegisterMappingStrategies(IServiceCollection services)
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
    }

    private static void ConfigureRabbitMqTransport(
        IBusRegistrationConfigurator registration,
        RabbitMqOptions options)
    {
        registration.UsingRabbitMq((context, bus) =>
        {
            bus.Host(options.Host, options.Port, options.VirtualHost, host =>
            {
                host.Username(options.Username);
                host.Password(options.Password);
            });

            bus.UseMessageRetry(retry => retry.Intervals(RetryIntervals));
            bus.UseFinanceIdempotency(context);
            bus.ConfigureEndpoints(context);
        });
    }

    private static RabbitMqOptions RequireRabbitMqOptions(IConfiguration configuration)
    {
        RabbitMqOptions options = new();
        configuration.GetSection(RabbitMqOptions.SectionName).Bind(options);

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            throw new InvalidOperationException(
                $"RabbitMQ:Host is required for the EventLog message bus. "
                + $"Code={MessagingErrorCodes.RABBITMQ_UNREACHABLE}.");
        }

        return options;
    }
}
