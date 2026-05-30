using Finance.Infrastructure.Messaging.Filters;
using MassTransit;

namespace Finance.Infrastructure.Messaging;

/// <summary>
/// Configurator extensions that wire the open-generic <see cref="IdempotencyFilter{T}"/> into a
/// MassTransit consume pipeline (SDD-INFRA-006 §2.5) so every consumer transparently skips replayed
/// messages via the shared Redis multiplexer.
/// </summary>
public static class FinanceIdempotencyConfiguratorExtensions
{
    /// <summary>
    /// Adds the Finance idempotency consume filter to a bus consume pipeline (RabbitMQ transport). Call
    /// inside the <c>UsingRabbitMq</c> delegate so it applies to every receive endpoint.
    /// </summary>
    /// <param name="configurator">The RabbitMQ bus-factory configurator being built.</param>
    /// <param name="registration">The bus registration context used to resolve filter dependencies.</param>
    public static void UseFinanceIdempotency(
        this IRabbitMqBusFactoryConfigurator configurator,
        IRegistrationContext registration)
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(registration);

        configurator.UseConsumeFilter(typeof(IdempotencyFilter<>), registration);
    }
}
