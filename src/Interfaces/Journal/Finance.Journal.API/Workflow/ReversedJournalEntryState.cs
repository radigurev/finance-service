using Finance.Common.Enums;
using Finance.Common.Workflow;
using Finance.Journal.DBModel.Models;

namespace Finance.Journal.API.Workflow;

/// <summary>
/// The terminal <c>Reversed</c> workflow state of a <see cref="JournalEntry"/> (SDD-FIN-002 §2.1). A
/// reversed original is final: it has no allowed next states. To correct further, reverse the reversal
/// entry (which is itself <c>Posted</c>).
/// </summary>
public sealed class ReversedJournalEntryState : IWorkflowState<JournalEntry>
{
    /// <inheritdoc />
    public string StateName => nameof(JournalEntryStatus.Reversed);

    /// <inheritdoc />
    public IReadOnlySet<string> AllowedNextStates { get; } =
        new HashSet<string>(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task OnEnterAsync(WorkflowContext<JournalEntry> context, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task OnExitAsync(WorkflowContext<JournalEntry> context, CancellationToken ct) => Task.CompletedTask;
}
