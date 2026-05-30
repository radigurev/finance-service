using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.Infrastructure.Messaging;

/// <summary>
/// Test-only registration helper that wires the MassTransit in-memory <c>TestHarness</c> so consumers and
/// publishers can be exercised without a real RabbitMQ broker, real Redis, or real SQL Server outbox
/// tables (SDD-INFRA-006 §3, §6). Production code MUST use
/// <see cref="MessagingServiceCollectionExtensions.AddFinanceMessageBus{TDbContext}"/> instead.
/// </summary>
public static class FinanceMessagingTestHarnessExtensions
{
    /// <summary>
    /// Registers the MassTransit test harness with the in-memory transport, applying the supplied
    /// <paramref name="configure"/> delegate to add consumers under test. The idempotency filter and
    /// outbox are intentionally omitted — the in-memory harness needs neither Redis nor SQL Server.
    /// </summary>
    /// <param name="services">The test service collection to register into.</param>
    /// <param name="configure">Optional delegate to register consumers, sagas, or request clients under test.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddFinanceMessagingTestHarness(
        this IServiceCollection services,
        Action<IBusRegistrationConfigurator>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMassTransitTestHarness(registration =>
        {
            configure?.Invoke(registration);
        });

        return services;
    }
}
