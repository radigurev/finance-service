using Finance.Common.Enums;
using Finance.Common.Workflow;
using Finance.Journal.DBModel.Models;

namespace Finance.Journal.API.Workflow;

/// <summary>
/// The <c>Draft</c> workflow state of a <see cref="JournalEntry"/> (SDD-FIN-002 §2.1). A draft may
/// transition only to <c>Posted</c> (deletion is a removal, not a transition). State entry/exit run no
/// side effects: posting side effects (number, stamps, audit, outbox, history) are owned by the calling
/// service inside the outbox transaction (SDD-INFRA-008 §2.2).
/// </summary>
public sealed class DraftJournalEntryState : IWorkflowState<JournalEntry>
{
    /// <inheritdoc />
    public string StateName => nameof(JournalEntryStatus.Draft);

    /// <inheritdoc />
    public IReadOnlySet<string> AllowedNextStates { get; } =
        new HashSet<string>(StringComparer.Ordinal) { nameof(JournalEntryStatus.Posted) };

    /// <inheritdoc />
    public Task OnEnterAsync(WorkflowContext<JournalEntry> context, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task OnExitAsync(WorkflowContext<JournalEntry> context, CancellationToken ct) => Task.CompletedTask;
}
