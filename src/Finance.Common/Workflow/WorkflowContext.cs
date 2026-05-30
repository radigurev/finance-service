namespace Finance.Common.Workflow;

/// <summary>
/// Carries the aggregate, the requested target state, and request metadata through a single
/// workflow transition handled by <see cref="IWorkflowEngine{TAggregate}"/>.
/// </summary>
/// <typeparam name="TAggregate">The aggregate type whose lifecycle is being transitioned.</typeparam>
public sealed class WorkflowContext<TAggregate>
{
    /// <summary>The aggregate instance being transitioned.</summary>
    public required TAggregate Aggregate { get; init; }

    /// <summary>The name of the state the aggregate is currently in (resolved by the engine before transitioning).</summary>
    public required string CurrentState { get; init; }

    /// <summary>The name of the state the aggregate is being moved to.</summary>
    public required string TargetState { get; init; }

    /// <summary>Optional human-supplied reason; required for sensitive transitions per audit rules.</summary>
    public string? Reason { get; init; }

    /// <summary>The ambient correlation identifier carried onto status-history and emitted events.</summary>
    public required string CorrelationId { get; init; }
}
