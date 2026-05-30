namespace Finance.Common.Workflow;

/// <summary>
/// One concrete state in an aggregate's lifecycle. Declares the legal next states and the
/// side effects to run on entry and exit. State-entry/-exit effects either succeed or throw;
/// guard validation is reported by the validation chain, not by these hooks.
/// <para>Orchestrated by <see cref="IWorkflowEngine{TAggregate}"/>.</para>
/// </summary>
/// <typeparam name="TAggregate">The aggregate type whose lifecycle this state belongs to.</typeparam>
public interface IWorkflowState<TAggregate>
{
    /// <summary>The state name; MUST match the enum value stored on the aggregate.</summary>
    string StateName { get; }

    /// <summary>The hard whitelist of states reachable from this state via the engine.</summary>
    IReadOnlySet<string> AllowedNextStates { get; }

    /// <summary>Runs side effects when the aggregate enters this state (publish event, allocate number, audit).</summary>
    /// <param name="context">The transition context.</param>
    /// <param name="ct">A token to observe for cancellation.</param>
    /// <returns>A task that completes when entry side effects finish.</returns>
    Task OnEnterAsync(WorkflowContext<TAggregate> context, CancellationToken ct);

    /// <summary>Runs side effects when the aggregate leaves this state.</summary>
    /// <param name="context">The transition context.</param>
    /// <param name="ct">A token to observe for cancellation.</param>
    /// <returns>A task that completes when exit side effects finish.</returns>
    Task OnExitAsync(WorkflowContext<TAggregate> context, CancellationToken ct);
}
