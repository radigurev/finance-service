using Finance.Common.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.Infrastructure.Services.Workflow;

/// <summary>
/// DI registration for the workflow engine. Registers the per-aggregate state registry and the
/// concrete <see cref="WorkflowEngine{TAggregate}"/> for a given aggregate (SDD-INFRA-008 §1).
/// </summary>
public static class WorkflowEngineServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="WorkflowStateRegistry{TAggregate}"/> (built from all registered
    /// <see cref="IWorkflowState{TAggregate}"/> implementations) and the
    /// <see cref="IWorkflowEngine{TAggregate}"/> for <typeparamref name="TAggregate"/>. Registering
    /// two states with the same name for the same aggregate fails when the registry is first resolved.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate type whose workflow is being wired.</typeparam>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddWorkflowEngine<TAggregate>(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(provider => new WorkflowStateRegistry<TAggregate>(
            provider.GetServices<IWorkflowState<TAggregate>>()));
        services.AddScoped<IWorkflowEngine<TAggregate>, WorkflowEngine<TAggregate>>();
        return services;
    }
}
