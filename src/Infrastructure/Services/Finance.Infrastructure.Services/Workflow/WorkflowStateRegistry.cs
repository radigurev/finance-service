using Finance.Common.Workflow;

namespace Finance.Infrastructure.Services.Workflow;

/// <summary>
/// A per-aggregate registry resolving <see cref="IWorkflowState{TAggregate}"/> implementations by
/// their <see cref="IWorkflowState{TAggregate}.StateName"/>. Built once at startup from the registered
/// state set; a duplicate state name for the same aggregate fails fast (SDD-INFRA-008 §3).
/// </summary>
/// <typeparam name="TAggregate">The aggregate type whose states this registry indexes.</typeparam>
public sealed class WorkflowStateRegistry<TAggregate>
{
    private readonly IReadOnlyDictionary<string, IWorkflowState<TAggregate>> _statesByName;

    /// <summary>
    /// Builds the registry from the supplied states, throwing when two states declare the same name.
    /// </summary>
    /// <param name="states">The registered workflow state implementations for the aggregate.</param>
    /// <exception cref="InvalidOperationException">When two states share a <c>StateName</c>.</exception>
    public WorkflowStateRegistry(IEnumerable<IWorkflowState<TAggregate>> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        Dictionary<string, IWorkflowState<TAggregate>> map = new(StringComparer.Ordinal);
        foreach (IWorkflowState<TAggregate> state in states)
        {
            if (!map.TryAdd(state.StateName, state))
            {
                throw new InvalidOperationException(
                    $"Duplicate workflow state '{state.StateName}' registered for aggregate '{typeof(TAggregate).Name}'.");
            }
        }

        _statesByName = map;
    }

    /// <summary>Attempts to resolve the state with the supplied name.</summary>
    /// <param name="stateName">The state name to resolve.</param>
    /// <param name="state">The resolved state when found; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a state with the name is registered; otherwise <see langword="false"/>.</returns>
    public bool TryGet(string stateName, out IWorkflowState<TAggregate>? state)
    {
        return _statesByName.TryGetValue(stateName, out state);
    }
}
