using Finance.Common.Results;

namespace Finance.Common.Workflow;

/// <summary>
/// Orchestrates a single legal state transition for an aggregate: verifies the target is allowed,
/// runs guards, invokes exit/entry side effects, and surfaces the outcome as a <see cref="Result"/>.
/// <para>The concrete implementation (EF Core based) ships in <c>Finance.Infrastructure.Services</c>.</para>
/// </summary>
/// <typeparam name="TAggregate">The aggregate type whose lifecycle is managed.</typeparam>
public interface IWorkflowEngine<TAggregate>
{
    /// <summary>Performs the transition described by the context, returning success or a failure code.</summary>
    /// <param name="context">The transition context carrying the aggregate, target state, and metadata.</param>
    /// <param name="ct">A token to observe for cancellation.</param>
    /// <returns>A <see cref="Result"/> describing the transition outcome.</returns>
    Task<Result> TransitionAsync(WorkflowContext<TAggregate> context, CancellationToken ct);
}
