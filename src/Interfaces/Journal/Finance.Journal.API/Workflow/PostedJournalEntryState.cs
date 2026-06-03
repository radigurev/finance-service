using Finance.Common.Enums;
using Finance.Common.Workflow;
using Finance.Journal.DBModel.Models;

namespace Finance.Journal.API.Workflow;

/// <summary>
/// The <c>Posted</c> workflow state of a <see cref="JournalEntry"/> (SDD-FIN-002 §2.1). A posted entry is
/// immutable except for the single transition to <c>Reversed</c>. State entry/exit run no side effects:
/// reversal side effects are owned by the calling service inside the outbox transaction
/// (SDD-INFRA-008 §2.2).
/// </summary>
public sealed class PostedJournalEntryState : IWorkflowState<JournalEntry>
{
    /// <inheritdoc />
    public string StateName => nameof(JournalEntryStatus.Posted);

    /// <inheritdoc />
    public IReadOnlySet<string> AllowedNextStates { get; } =
        new HashSet<string>(StringComparer.Ordinal) { nameof(JournalEntryStatus.Reversed) };

    /// <inheritdoc />
    public Task OnEnterAsync(WorkflowContext<JournalEntry> context, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task OnExitAsync(WorkflowContext<JournalEntry> context, CancellationToken ct) => Task.CompletedTask;
}
