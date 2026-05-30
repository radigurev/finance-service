using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Common.Validation;
using Finance.Common.Workflow;

namespace Finance.Infrastructure.Services.Workflow;

/// <summary>
/// Concrete state-machine orchestrator for an aggregate's lifecycle. Validates the requested
/// transition against the current state's allowed next states, runs registered guards, and invokes
/// exit/entry side effects, surfacing the outcome as a <see cref="Result"/>. In v1 the engine does
/// NOT persist — the calling service owns <c>SaveChanges</c>, the <c>RowVersion</c> increment, and the
/// status-history append (SDD-INFRA-008 §2.2).
/// </summary>
/// <typeparam name="TAggregate">The aggregate type whose lifecycle is managed.</typeparam>
public sealed class WorkflowEngine<TAggregate> : IWorkflowEngine<TAggregate>
{
    private readonly WorkflowStateRegistry<TAggregate> _registry;
    private readonly IReadOnlyList<IChainValidator<WorkflowContext<TAggregate>>> _guards;

    /// <summary>Initializes the engine with the per-aggregate state registry and transition guards.</summary>
    /// <param name="registry">The per-aggregate state registry built at startup.</param>
    /// <param name="guards">The registered transition guards run before any side effects.</param>
    public WorkflowEngine(
        WorkflowStateRegistry<TAggregate> registry,
        IEnumerable<IChainValidator<WorkflowContext<TAggregate>>> guards)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(guards);
        _registry = registry;
        _guards = [.. guards];
    }

    /// <inheritdoc />
    public async Task<Result> TransitionAsync(WorkflowContext<TAggregate> context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_registry.TryGet(context.CurrentState, out IWorkflowState<TAggregate>? current) ||
            !_registry.TryGet(context.TargetState, out IWorkflowState<TAggregate>? target))
        {
            return Result.Failure(WorkflowErrorCodes.STATE_NOT_REGISTERED);
        }

        if (!current!.AllowedNextStates.Contains(context.TargetState))
        {
            return Result.Failure(WorkflowErrorCodes.INVALID_STATE_TRANSITION);
        }

        Result guardResult = await RunGuardsAsync(context, ct).ConfigureAwait(false);
        if (!guardResult.IsSuccess)
        {
            return guardResult;
        }

        await current.OnExitAsync(context, ct).ConfigureAwait(false);
        await target!.OnEnterAsync(context, ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result> RunGuardsAsync(WorkflowContext<TAggregate> context, CancellationToken ct)
    {
        foreach (IChainValidator<WorkflowContext<TAggregate>> guard in _guards)
        {
            ChainValidationResult validation = await guard.ValidateAsync(context, ct).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                return Result.Failure(WorkflowErrorCodes.WORKFLOW_GUARD_FAILED, validation.ErrorCode);
            }
        }

        return Result.Success();
    }
}
