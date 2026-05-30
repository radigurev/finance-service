using Finance.Common.Workflow;

namespace Finance.Infrastructure.Tests.Services.Fixtures.Workflow;

/// <summary>
/// A test <see cref="IWorkflowState{TAggregate}"/> for <see cref="SampleAggregate"/> that records the
/// order in which <c>OnExitAsync</c> and <c>OnEnterAsync</c> are invoked into a shared log.
/// </summary>
public sealed class RecordingWorkflowState : IWorkflowState<SampleAggregate>
{
    private readonly List<string> _log;

    /// <summary>Initializes the state with its name, allowed next states, and the shared call log.</summary>
    /// <param name="stateName">The state name.</param>
    /// <param name="allowedNextStates">The allowed next states.</param>
    /// <param name="log">The shared list recording entry/exit calls.</param>
    public RecordingWorkflowState(string stateName, IReadOnlySet<string> allowedNextStates, List<string> log)
    {
        StateName = stateName;
        AllowedNextStates = allowedNextStates;
        _log = log;
    }

    /// <inheritdoc />
    public string StateName { get; }

    /// <inheritdoc />
    public IReadOnlySet<string> AllowedNextStates { get; }

    /// <inheritdoc />
    public Task OnEnterAsync(WorkflowContext<SampleAggregate> context, CancellationToken ct)
    {
        _log.Add($"enter:{StateName}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnExitAsync(WorkflowContext<SampleAggregate> context, CancellationToken ct)
    {
        _log.Add($"exit:{StateName}");
        return Task.CompletedTask;
    }
}
