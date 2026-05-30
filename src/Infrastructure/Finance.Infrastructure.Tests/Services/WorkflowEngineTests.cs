using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Common.Validation;
using Finance.Common.Workflow;
using Finance.Infrastructure.Services.Workflow;
using Finance.Infrastructure.Tests.Services.Fixtures.Workflow;
using NUnit.Framework;

namespace Finance.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for <see cref="WorkflowEngine{TAggregate}"/> covering legal/illegal transitions, guard
/// failures, unknown states, and duplicate-state startup validation (SDD-INFRA-008 §2.2, §3).
/// </summary>
[TestFixture]
[Category("SDD-INFRA-008")]
public sealed class WorkflowEngineTests
{
    private List<string> _log = null!;

    /// <summary>Resets the shared call log before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _log = [];
    }

    /// <summary>A target state in AllowedNextStates succeeds.</summary>
    [Test]
    public async Task Transition_AllowedNextState_Succeeds()
    {
        // Arrange
        WorkflowEngine<SampleAggregate> engine = BuildEngine([]);
        WorkflowContext<SampleAggregate> context = BuildContext("Draft", "Posted");

        // Act
        Result result = await engine.TransitionAsync(context, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
    }

    /// <summary>A legal transition runs OnExit on the current state then OnEnter on the target.</summary>
    [Test]
    public async Task Transition_RunsOnExitThenOnEnter()
    {
        // Arrange
        WorkflowEngine<SampleAggregate> engine = BuildEngine([]);
        WorkflowContext<SampleAggregate> context = BuildContext("Draft", "Posted");

        // Act
        await engine.TransitionAsync(context, CancellationToken.None);

        // Assert
        Assert.That(_log, Is.EqualTo(new[] { "exit:Draft", "enter:Posted" }));
    }

    /// <summary>A target state not in AllowedNextStates returns INVALID_STATE_TRANSITION.</summary>
    [Test]
    public async Task Transition_DisallowedNextState_ReturnsInvalidStateTransition()
    {
        // Arrange
        WorkflowEngine<SampleAggregate> engine = BuildEngine([]);
        WorkflowContext<SampleAggregate> context = BuildContext("Posted", "Draft");

        // Act
        Result result = await engine.TransitionAsync(context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(WorkflowErrorCodes.INVALID_STATE_TRANSITION));
            Assert.That(_log, Is.Empty);
        });
    }

    /// <summary>A failing guard short-circuits with WORKFLOW_GUARD_FAILED and runs no side effects.</summary>
    [Test]
    public async Task Transition_GuardFailure_ReturnsWorkflowGuardFailed_NoSideEffects()
    {
        // Arrange
        WorkflowEngine<SampleAggregate> engine = BuildEngine([new RejectingGuard()]);
        WorkflowContext<SampleAggregate> context = BuildContext("Draft", "Posted");

        // Act
        Result result = await engine.TransitionAsync(context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(WorkflowErrorCodes.WORKFLOW_GUARD_FAILED));
            Assert.That(result.Detail, Is.EqualTo(RejectingGuard.GuardErrorCode));
            Assert.That(_log, Is.Empty);
        });
    }

    /// <summary>An unregistered current or target state returns STATE_NOT_REGISTERED.</summary>
    [Test]
    public async Task Transition_UnknownState_ReturnsStateNotRegistered()
    {
        // Arrange
        WorkflowEngine<SampleAggregate> engine = BuildEngine([]);
        WorkflowContext<SampleAggregate> context = BuildContext("Draft", "Archived");

        // Act
        Result result = await engine.TransitionAsync(context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(WorkflowErrorCodes.STATE_NOT_REGISTERED));
        });
    }

    /// <summary>Building the registry with duplicate state names for one aggregate fails at startup.</summary>
    [Test]
    public void Engine_FailsAtStartup_WhenDuplicateStateNamesRegistered()
    {
        // Arrange
        List<IWorkflowState<SampleAggregate>> states =
        [
            new RecordingWorkflowState("Draft", new HashSet<string> { "Posted" }, _log),
            new RecordingWorkflowState("Draft", new HashSet<string> { "Posted" }, _log)
        ];

        // Act
        TestDelegate act = () => _ = new WorkflowStateRegistry<SampleAggregate>(states);

        // Assert
        Assert.That(
            act,
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("Duplicate workflow state 'Draft'"));
    }

    private WorkflowEngine<SampleAggregate> BuildEngine(
        IEnumerable<IChainValidator<WorkflowContext<SampleAggregate>>> guards)
    {
        List<IWorkflowState<SampleAggregate>> states =
        [
            new RecordingWorkflowState("Draft", new HashSet<string> { "Posted" }, _log),
            new RecordingWorkflowState("Posted", new HashSet<string> { "Reversed" }, _log),
            new RecordingWorkflowState("Reversed", new HashSet<string>(), _log)
        ];
        WorkflowStateRegistry<SampleAggregate> registry = new(states);
        return new WorkflowEngine<SampleAggregate>(registry, guards);
    }

    private static WorkflowContext<SampleAggregate> BuildContext(string currentState, string targetState)
    {
        return new WorkflowContext<SampleAggregate>
        {
            Aggregate = new SampleAggregate { State = currentState },
            CurrentState = currentState,
            TargetState = targetState,
            CorrelationId = "test-correlation-id"
        };
    }
}
