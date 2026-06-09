using Finance.Common.ErrorCodes;
using Finance.Infrastructure.Messaging.Configuration;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.Infrastructure.Messaging;

/// <summary>
/// Dependency-injection registration for the Finance resilient message bus (SDD-INFRA-006).
/// Wires MassTransit with the EF Core transactional outbox bound to the publishing service DbContext,
/// the RabbitMQ transport, the retry/dead-letter policy, and the Redis-backed idempotency filter that
/// reuses the <c>IConnectionMultiplexer</c> owned by <c>Finance.Infrastructure.Caching</c> (SDD-INFRA-004).
/// The physical outbox tables (<c>OutboxMessage</c>/<c>OutboxState</c>/<c>InboxState</c>) and concrete
/// domain events are owned per publishing service later (Batch 4+); this library ships the wiring only.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    private static readonly TimeSpan OutboxQueryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan OutboxDuplicateDetectionWindow = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan[] RetryIntervals =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15)
    ];

    /// <summary>
    /// Registers the Finance message bus for a publishing service whose DbContext is
    /// <typeparamref name="TDbContext"/>. Validates that <c>RabbitMQ:Host</c> and
    /// <c>ConnectionStrings:Redis</c> are present (SDD-INFRA-006 §3), binds the EF Core outbox to the
    /// context, and configures retry/dead-letter and idempotency on the RabbitMQ transport.
    /// </summary>
    /// <typeparam name="TDbContext">The publishing service DbContext that owns the outbox tables.</typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">Configuration carrying the <c>RabbitMQ</c> section and <c>ConnectionStrings:Redis</c>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddFinanceMessageBus<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TDbContext : DbContext
    {
        return services.AddFinanceMessageBus<TDbContext>(configuration, configureConsumers: null);
    }

    /// <summary>
    /// Registers the Finance message bus for a publishing service that ALSO consumes events. Identical to
    /// <see cref="AddFinanceMessageBus{TDbContext}(IServiceCollection, IConfiguration)"/> but invokes
    /// <paramref name="configureConsumers"/> so the caller can register its consumers (each wrapped by the
    /// shared idempotency filter) inside the same MassTransit registration as the EF outbox (SDD-INFRA-006).
    /// </summary>
    /// <typeparam name="TDbContext">The publishing service DbContext that owns the outbox tables.</typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">Configuration carrying the <c>RabbitMQ</c> section and <c>ConnectionStrings:Redis</c>.</param>
    /// <param name="configureConsumers">An optional delegate to register consumers on the MassTransit configurator.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddFinanceMessageBus<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureConsumers)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        RabbitMqOptions options = RequireRabbitMqOptions(configuration);
        RequireRedisConnectionString(configuration);

        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));

        services.AddMassTransit(registration =>
        {
            ConfigureOutbox<TDbContext>(registration);
            configureConsumers?.Invoke(registration);
            ConfigureRabbitMqTransport(registration, options);
        });

        return services;
    }

    /// <summary>
    /// Configures the EF Core transactional outbox for <typeparamref name="TDbContext"/> with the standard
    /// query delay and duplicate-detection window (SDD-INFRA-006 §2.1, §2.6). The MassTransit 8.3.0 EF Core
    /// outbox configurator does not expose a delivered-message retention setter, so the delivered-message
    /// purge runs on MassTransit's built-in delivery-service defaults.
    /// </summary>
    /// <typeparam name="TDbContext">The DbContext that owns the outbox tables.</typeparam>
    /// <param name="registration">The MassTransit registration configurator.</param>
    private static void ConfigureOutbox<TDbContext>(IBusRegistrationConfigurator registration)
        where TDbContext : DbContext
    {
        registration.AddEntityFrameworkOutbox<TDbContext>(outbox =>
        {
            outbox.UseSqlServer();
            outbox.UseBusOutbox();
            outbox.QueryDelay = OutboxQueryDelay;
            outbox.DuplicateDetectionWindow = OutboxDuplicateDetectionWindow;
            outbox.DisableInboxCleanupService();
        });
    }

    /// <summary>
    /// Configures the RabbitMQ transport: host wiring, the retry/dead-letter policy, the Finance
    /// idempotency consume filter, and endpoint conventions (SDD-INFRA-006 §2.3-§2.5).
    /// </summary>
    /// <param name="registration">The MassTransit registration configurator.</param>
    /// <param name="options">The validated RabbitMQ transport options.</param>
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

    /// <summary>
    /// Binds and validates the <c>RabbitMQ</c> options, throwing when <c>RabbitMQ:Host</c> is missing
    /// so startup fails fast (SDD-INFRA-006 §3).
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The validated RabbitMQ options.</returns>
    private static RabbitMqOptions RequireRabbitMqOptions(IConfiguration configuration)
    {
        RabbitMqOptions options = new RabbitMqOptions();
        configuration.GetSection(RabbitMqOptions.SectionName).Bind(options);

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            throw new InvalidOperationException(
                $"RabbitMQ:Host is required for the Finance message bus. "
                + $"Code={MessagingErrorCodes.RABBITMQ_UNREACHABLE}.");
        }

        return options;
    }

    /// <summary>
    /// Validates that <c>ConnectionStrings:Redis</c> is present, since the idempotency filter depends on
    /// the Redis multiplexer owned by the Caching library (SDD-INFRA-006 §3).
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    private static void RequireRedisConnectionString(IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:Redis is required for the Finance message bus idempotency filter. "
                + $"Code={MessagingErrorCodes.RABBITMQ_UNREACHABLE}.");
        }
    }
}
